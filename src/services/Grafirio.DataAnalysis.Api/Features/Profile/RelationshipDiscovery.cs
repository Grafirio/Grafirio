using System.Data;

using Dapper;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>
/// Tablolar arasi baglantilari bulur: hangi kolon hangi tablonun anahtarina
/// isaret ediyor.
///
/// Neden gerekli: kullanicinin gordugu sey kod, aradigi sey isim. Bugun
/// calistigimiz tabloda kodun yaninda adi da duruyor ama bu istisna — normalde
/// <c>ReceiverCompanyId</c> var, <c>Companies.Unvan</c> baska tabloda. Yolu
/// takip edemezsek o soru cevaplanamaz.
///
/// Uc kaynaktan besleniyor:
///
///   1. Bildirilmis yabanci anahtarlar. Guvenilir, ama cogu uretim
///      veritabaninda ya hic yok ya da eksik.
///   2. Ad kaliplarindan cikarim. Yalnizca ADAY uretir, kanit degildir.
///   3. Deger ortusmesi. Asil kanit bu: cocuk kolondaki degerlerin kacta kaci
///      gercekten ebeveyn anahtarda var?
///
/// Cikarim adimi tek basina birakilirsa <c>SiparisNo</c> ile <c>Siparisler.No</c>
/// arasinda olmayan bir baglanti kurabilir; deger ortusmesi bunu eler. Ters
/// yonde de calisir: adi hic benzemeyen iki kolon arasindaki gercek baglantiyi
/// ad kalibi bulamaz, o yuzden bulunamayan baglanti icin kullaniciya sorulur
/// (bu adim Faz 2'de).
/// </summary>
public class RelationshipDiscovery(ILogger<RelationshipDiscovery> logger)
{
    /// <summary>Deger ortusmesi olculurken cocuk kolondan alinan deger sayisi.</summary>
    private const int OverlapSampleSize = 200;

    /// <summary>Bu oranin altindaki aday baglanti kabul edilmez.</summary>
    private const double MinOverlap = 0.60;

    /// <summary>Bu oranin ustunde baglanti kullaniciya sorulmadan kabul edilir.</summary>
    private const double HighConfidenceOverlap = 0.90;

    public async Task<List<RelationshipProfile>> DiscoverAsync(
        SqlConnection connection,
        IReadOnlyList<TableProfile> tables,
        CancellationToken ct = default)
    {
        var usable = tables.Where(t => t.Error is null && t.Columns.Count > 0).ToList();
        if (usable.Count == 0) return [];

        var names = usable.Select(t => t.Qualified).ToList();

        // Tekil anahtarlar once cekiliyor: bir kolonun "arama hedefi"
        // olabilmesi icin benzersiz olmasi sart. Bu hem dogruluk kosulu
        // (benzersiz olmayan hedefe join satirlari cogaltir) hem de hiz kosulu
        // — ortusme olcumu ancak indeksli bir kolonda seek olur.
        var uniqueColumns = await FetchUniqueColumnsAsync(connection, names, ct);

        var declared = await FetchDeclaredForeignKeysAsync(connection, names, ct);
        logger.LogInformation("Bildirilmiş yabancı anahtar: {Count}", declared.Count);

        var edges = new List<RelationshipProfile>(declared);
        var seen = declared.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in BuildCandidates(usable, uniqueColumns))
        {
            if (!seen.Add(Key(candidate))) continue;

            var overlap = await MeasureOverlapAsync(connection, candidate, ct);
            if (overlap is null) continue;

            candidate.ValueOverlap = Math.Round(overlap.Value, 3);

            if (overlap < MinOverlap)
            {
                logger.LogDebug("Aday elendi ({Overlap:P0}): {Edge}", overlap, Key(candidate));
                continue;
            }

            candidate.Confidence = overlap >= HighConfidenceOverlap ? "high" : "medium";
            candidate.Note = $"Değerlerin %{overlap * 100:F0}'ı hedef tabloda bulundu.";
            edges.Add(candidate);
        }

        AttachLabelColumns(edges, usable);

        logger.LogInformation(
            "İlişki keşfi tamamlandı: {Total} bağlantı ({Declared} bildirilmiş, {Inferred} çıkarsanmış)",
            edges.Count, declared.Count, edges.Count - declared.Count);

        return edges;
    }

    private static string Key(RelationshipProfile r) =>
        $"{r.FromTable}({string.Join(",", r.FromColumns)})->{r.ToTable}";

    /* ── 1. Bildirilmis yabanci anahtarlar ────────────────────────────── */

    /// <summary>
    /// Onceki surum tablolari <c>OBJECT_NAME(...) IN @Names</c> ile, yani sema
    /// adi olmadan esliyordu: farkli semalarda ayni adli iki tablo varsa
    /// birbirine karisiyordu ve donen kayitlar da semasiz oldugu icin sistemin
    /// geri kalaniyla (her yerde <c>sema.tablo</c>) eslesmiyordu.
    ///
    /// Ayrica iki alan daha okunuyor: <c>is_not_trusted</c> ve kolonun
    /// nullable olup olmadigi. Ikisi birden JOIN tipini belirleyecek — yalnizca
    /// guvenilir ve zorunlu bir FK'de INNER JOIN satir kaybettirmez.
    /// </summary>
    private static async Task<List<RelationshipProfile>> FetchDeclaredForeignKeysAsync(
        SqlConnection connection, List<string> names, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<ForeignKeyRow>(
            new CommandDefinition(@"
                SELECT
                    fk.object_id                        AS ConstraintId,
                    ps.name + '.' + po.name             AS FromTable,
                    pc.name                             AS FromColumn,
                    rs.name + '.' + ro.name             AS ToTable,
                    rc.name                             AS ToColumn,
                    fk.is_not_trusted                   AS IsNotTrusted,
                    pc.is_nullable                      AS IsNullable,
                    fkc.constraint_column_id            AS Ordinal
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                JOIN sys.objects po ON po.object_id = fk.parent_object_id
                JOIN sys.schemas ps ON ps.schema_id = po.schema_id
                JOIN sys.objects ro ON ro.object_id = fk.referenced_object_id
                JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
                JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id
                                   AND pc.column_id = fkc.parent_column_id
                JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id
                                   AND rc.column_id = fkc.referenced_column_id
                WHERE ps.name + '.' + po.name IN @Names
                  AND rs.name + '.' + ro.name IN @Names
                ORDER BY fk.object_id, fkc.constraint_column_id",
                new { Names = names }, commandTimeout: 60, cancellationToken: ct));

        // Bilesik anahtarlar birden fazla satir doner; tek kenara toplaniyor.
        return rows
            .GroupBy(r => r.ConstraintId)
            .Select(g =>
            {
                var first = g.First();
                return new RelationshipProfile
                {
                    FromTable = first.FromTable,
                    FromColumns = g.Select(r => r.FromColumn).ToList(),
                    ToTable = first.ToTable,
                    ToColumns = g.Select(r => r.ToColumn).ToList(),
                    Cardinality = RelationshipProfile.ManyToOne,
                    // Kolonlardan biri bile bos olabiliyorsa ya da kisit
                    // dogrulanmamissa, eslesmeyen satir mumkundur.
                    IsOptional = g.Any(r => r.IsNullable) || first.IsNotTrusted,
                    IsTrusted = !first.IsNotTrusted,
                    Source = "fk",
                    Confidence = "high",
                };
            })
            .ToList();
    }

    /* ── 2. Ad kaliplarindan aday uretimi ─────────────────────────────── */

    /// <summary>
    /// Ad kaliplarindan aday baglantilar uretir. Saf: veritabanina dokunmaz.
    ///
    /// Public olmasinin sebebi test edilebilirlik. Bu metot "hangi kolon hangi
    /// tabloya isaret ediyor" tahminini yapan yer; yanlis calistiginda sistem
    /// sessizce yanlis tabloya baglanir. Gercek bir sema uzerinde ciktisinin
    /// gozle denetlenebilmesi gerekiyor.
    ///
    /// Urettigi her sey ADAYDIR — kanit degil. Deger ortusmesinden gecmeden
    /// hicbiri kabul edilmez.
    /// </summary>
    public static IEnumerable<RelationshipProfile> BuildCandidates(
        IReadOnlyList<TableProfile> tables,
        IReadOnlyDictionary<string, HashSet<string>> uniqueColumns)
    {
        // Her tablonun kuyruk parcalari onceden cikariliyor: sistem onekli
        // adlar (L_INT_ExportReference) yalnizca tam adla eslesmiyor.
        var tableSegments = tables
            .Select(t => (Table: t, Segments: RelationshipNaming.NameSegments(t.TableName).ToList()))
            .ToList();

        foreach (var table in tables)
        {
            var sourceKeys = uniqueColumns.GetValueOrDefault(table.Qualified) ?? [];

            foreach (var column in table.Columns)
            {
                var stem = RelationshipNaming.StripReferenceSuffix(column.ColumnName);
                if (stem is null) continue;

                // Tablonun KENDI anahtari bir referans degildir. Bu eleme
                // olmadan L_INT_ExportReference.ReferenceId kolonu kendi
                // tablosunu isaret eden anlamsiz bir kenar uretiyordu.
                if (sourceKeys.Contains(column.ColumnName)) continue;

                // "ReceiverCompanyId" -> once "ReceiverCompany", sonra "Company".
                // Rol oneki tasiyan kolonlar (gonderici/alici) boyle cozuluyor.
                foreach (var attempt in RelationshipNaming.TailSegments(stem))
                {
                    // En UZUN eslesen tablo parcasi kazanir: "ExportReference"
                    // eslesmesi "Reference" eslesmesine tercih edilir, yoksa
                    // birden fazla tablo ayni kisa ada indiginde secim rastgele
                    // olurdu.
                    var target = tableSegments
                        .SelectMany(x => x.Segments.Select(s => (x.Table, Segment: s)))
                        .Where(x => RelationshipNaming.NamesMatch(x.Segment, attempt))
                        .OrderByDescending(x => x.Segment.Length)
                        .Select(x => x.Table)
                        .FirstOrDefault();

                    if (target is null) continue;

                    // Kendi tablosunu isaret eden aday uretilmiyor.
                    //
                    // Onceki koruma kolon adiyla ARANAN KOKU karsilastiriyordu
                    // ("ReferenceId" ile "Reference") — hicbir zaman esit
                    // olmadiklari icin hic devreye girmedi. Gercek semada bu,
                    // ReferenceNo -> ReferenceId gibi kenarlar uretiyordu:
                    // ikisi de ayni satirin kimligi, aralarinda gidilecek bir
                    // yol yok.
                    //
                    // Gercek oz-referanslar (ParentId, UstReferenceId) boylece
                    // kaciriliyor; bilincli bir tercih. Bildirilmis FK'ler
                    // onlari zaten yakaliyor, cikarimla uretilen oz-referans
                    // ise neredeyse her zaman yanlis pozitif.
                    if (ReferenceEquals(target, table)) break;

                    if (!uniqueColumns.TryGetValue(target.Qualified, out var keys) || keys.Count == 0)
                        continue;

                    var targetColumn = PickTargetColumn(column, target, keys);
                    if (targetColumn is null) continue;

                    var targetProfile = target.Columns.First(c =>
                        string.Equals(c.ColumnName, targetColumn, StringComparison.OrdinalIgnoreCase));

                    if (!RelationshipNaming.TypesCompatible(column.DataType, targetProfile.DataType))
                        continue;

                    var childIsUnique =
                        uniqueColumns.TryGetValue(table.Qualified, out var ownKeys)
                        && ownKeys.Contains(column.ColumnName);

                    yield return new RelationshipProfile
                    {
                        FromTable = table.Qualified,
                        FromColumns = [column.ColumnName],
                        ToTable = target.Qualified,
                        ToColumns = [targetColumn],
                        Cardinality = childIsUnique
                            ? RelationshipProfile.OneToOne
                            : RelationshipProfile.ManyToOne,
                        // Cikarsanmis kenarda referans butunlugu garantisi yok:
                        // her zaman opsiyonel sayilir, yani LEFT JOIN.
                        IsOptional = true,
                        IsTrusted = false,
                        Source = "inferred",
                    };

                    break; // en yakin eslesme kazanir
                }
            }
        }
    }

    private static string? PickTargetColumn(
        ColumnProfile source, TableProfile target, HashSet<string> uniqueKeys)
    {
        // Ayni adli benzersiz kolon en guclu isaret.
        var exact = uniqueKeys.FirstOrDefault(
            k => string.Equals(k, source.ColumnName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Sonra birincil anahtar.
        var primary = target.Columns.FirstOrDefault(
            c => c.IsPrimaryKey && uniqueKeys.Contains(c.ColumnName));
        if (primary is not null) return primary.ColumnName;

        // Son care: kolonun ekiyle ayni eki tasiyan benzersiz kolon
        // (SiparisKodu -> Siparisler.Kod gibi).
        return uniqueKeys.FirstOrDefault(k =>
            source.ColumnName.EndsWith(k, StringComparison.OrdinalIgnoreCase));
    }

    /* ── 3. Deger ortusmesi: asil kanit ───────────────────────────────── */

    /// <summary>
    /// Cocuk kolondaki degerlerin kacta kaci gercekten ebeveyn anahtarda var?
    ///
    /// Hedef kolon her zaman benzersiz (PK ya da unique index) oldugu icin
    /// <c>EXISTS</c> indeks aramasina duser; tam tarama olmaz.
    /// </summary>
    private async Task<double?> MeasureOverlapAsync(
        SqlConnection connection, RelationshipProfile candidate, CancellationToken ct)
    {
        var (fromSchema, fromTable) = Split(candidate.FromTable);
        var (toSchema, toTable) = Split(candidate.ToTable);

        var sql = $"""
            SELECT COUNT(*) AS Total,
                   SUM(CASE WHEN EXISTS (
                           SELECT 1 FROM {Quote(toSchema)}.{Quote(toTable)} p
                           WHERE p.{Quote(candidate.ToColumns[0])} = c.v)
                       THEN 1 ELSE 0 END) AS Matched
            FROM (SELECT DISTINCT TOP {OverlapSampleSize} {Quote(candidate.FromColumns[0])} AS v
                  FROM {Quote(fromSchema)}.{Quote(fromTable)}
                  WHERE {Quote(candidate.FromColumns[0])} IS NOT NULL) c
            """;

        try
        {
            var result = await connection.QuerySingleOrDefaultAsync<OverlapRow>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: ct));

            if (result is null || result.Total == 0) return null;
            return (double)result.Matched / result.Total;
        }
        catch (SqlException ex)
        {
            // Tip donusumu tutmayan aday burada patlar; bu bir hata degil,
            // adayin elenmesidir.
            logger.LogDebug(ex, "Örtüşme ölçülemedi: {Edge}", Key(candidate));
            return null;
        }
    }

    /* ── 4. Etiket kolonu: kodun okunabilir karsiligi ─────────────────── */

    /// <summary>
    /// Her hedef tablo icin "bu kaydin adi nedir" sorusunun cevabi.
    /// Kullanicinin grafikte kod yerine gormek istedigi kolon bu.
    /// </summary>
    private static void AttachLabelColumns(
        List<RelationshipProfile> edges, IReadOnlyList<TableProfile> tables)
    {
        var byQualified = tables.ToDictionary(t => t.Qualified, StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            if (!byQualified.TryGetValue(edge.ToTable, out var target)) continue;

            var candidates = target.Columns
                .Where(c => !c.IsPrimaryKey && RelationshipNaming.IsTextual(c.DataType))
                .ToList();

            // 1) Adi dogrudan "ad/isim" anlamina gelen kolon. Ipuclari sirali:
            //    "Unvan" varsa "Aciklama"ya dusulmemeli.
            var hinted = RelationshipNaming.LabelHints
                .Select(hint => candidates.FirstOrDefault(
                    c => c.ColumnName.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(c => c is not null);

            // 2) Yoksa: tekrar eden degerleri olan ilk metin kolonu — serbest
            //    metin degil, ad gibi davranan bir alan.
            hinted ??= candidates.FirstOrDefault(c => c.DistinctCount is > 0 and < 10000);

            edge.LabelColumn = hinted?.ColumnName;

            if (edge.LabelColumn is null)
                edge.Note = string.Join(" ",
                    new[] { edge.Note, "Hedef tabloda okunabilir bir ad kolonu bulunamadı." }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }

    /* ── Yardimcilar ──────────────────────────────────────────────────── */

    private static async Task<Dictionary<string, HashSet<string>>> FetchUniqueColumnsAsync(
        SqlConnection connection, List<string> names, CancellationToken ct)
    {
        // Yalnizca TEK kolonlu benzersiz indeksler: arama hedefi olabilmesi
        // icin kolonun kendi basina benzersiz olmasi gerekiyor.
        var rows = await connection.QueryAsync<UniqueColumnRow>(
            new CommandDefinition(@"
                SELECT s.name + '.' + t.name AS TableName, MIN(c.name) AS ColumnName
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id
                                         AND ic.index_id = i.index_id
                                         AND ic.is_included_column = 0
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                JOIN sys.tables t ON t.object_id = i.object_id
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE (i.is_primary_key = 1 OR i.is_unique = 1)
                  AND s.name + '.' + t.name IN @Names
                GROUP BY s.name, t.name, i.object_id, i.index_id
                HAVING COUNT(*) = 1",
                new { Names = names }, commandTimeout: 60, cancellationToken: ct));

        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.TableName, out var set))
                result[row.TableName] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(row.ColumnName);
        }

        return result;
    }

    private static (string Schema, string Table) Split(string qualified)
    {
        var parts = qualified.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? (parts[0], parts[^1]) : ("dbo", qualified);
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    private sealed class ForeignKeyRow
    {
        public int ConstraintId { get; set; }
        public string FromTable { get; set; } = "";
        public string FromColumn { get; set; } = "";
        public string ToTable { get; set; } = "";
        public string ToColumn { get; set; } = "";
        public bool IsNotTrusted { get; set; }
        public bool IsNullable { get; set; }
        public int Ordinal { get; set; }
    }

    private sealed class UniqueColumnRow
    {
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
    }

    private sealed class OverlapRow
    {
        public int Total { get; set; }
        public int Matched { get; set; }
    }
}

using Grafirio.DataAnalysis.Api.Data.Access;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>
/// Secili tablolarin profilini cikarir: sema, anahtarlar, kolon istatistikleri
/// ve — politikanin izin verdigi olcude — ornek degerler.
///
/// Kural: <b>yalnizca secili tablolar</b>. Her sorgu tablo listesine kisitli;
/// secili olmayan bir tabloya tek bir okuma bile gitmez.
///
/// Neden ornek deger: kolonun adi ne oldugunu soylemiyor.
/// `ReceiverCompanyCountryName` adindan "ulke" oldugu cikmayabilir, ama
/// icinde "Almanya", "Hollanda" gorununce belli oluyor. Esleme kalitesini
/// belirleyen asil sinyal bu.
/// </summary>
public class SchemaProfiler(ILogger<SchemaProfiler> logger, RelationshipDiscovery relationships)
{
    /// <summary>LLM'e gonderilecek ornek deger sayisi (kolon basina).</summary>
    private const int SampleSize = 20;

    /// <summary>Profil cikarilirken tablodan cekilen satir sayisi.</summary>
    private const int SampleRowCount = 1000;

    private const int LowCardinalityThreshold = 50;

    /// <summary>Ornek satir cekme sorgusunun zaman asimi (saniye).</summary>
    private const int SampleTimeoutSeconds = 60;

    public async Task<DatabaseProfile> ProfileAsync(
        IDataSourceSession session,
        string databaseName,
        IReadOnlyList<string> selectedTables,
        bool samplingConsentGiven,
        CancellationToken ct = default)
    {
        if (selectedTables.Count == 0)
            throw new InvalidOperationException("Tablo seçimi boş; profil çıkarılamaz.");

        var profile = new DatabaseProfile
        {
            DatabaseName = databaseName,
            SamplingConsentGiven = samplingConsentGiven
        };

        foreach (var qualified in selectedTables)
        {
            var (schema, table) = SplitTableName(qualified);
            try
            {
                profile.Tables.Add(await ProfileTableAsync(
                    session, schema, table, samplingConsentGiven, ct));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Tablo profillenemedi: {Schema}.{Table}", schema, table);
                profile.Tables.Add(new TableProfile
                {
                    Schema = schema,
                    TableName = table,
                    Error = ex.Message
                });
            }
        }

        // Iliskiler kolon profillerinden SONRA cikariliyor: cikarim adimi
        // kolon adlarina, tiplerine ve benzersizligine bakiyor.
        profile.Relationships = await relationships.DiscoverAsync(session, profile.Tables, ct);
        return profile;
    }

    private async Task<TableProfile> ProfileTableAsync(
        IDataSourceSession session, string schema, string table, bool consent, CancellationToken ct)
    {
        var result = new TableProfile { Schema = schema, TableName = table };

        // Yaklasik satir sayisi. COUNT(*) her tabloda tam tarama demekti;
        // profil icin kesin sayiya ihtiyac yok.
        result.ApproximateRowCount = await session.ScalarAsync<long?>(@"
                SELECT SUM(p.rows)
                FROM sys.partitions p
                JOIN sys.objects o ON o.object_id = p.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE s.name = @Schema AND o.name = @Table AND p.index_id IN (0, 1)",
            new { Schema = schema, Table = table }, ct: ct) ?? 0;

        var columns = await session.QueryAsync<ColumnRow>(@"
                SELECT c.COLUMN_NAME AS ColumnName, c.DATA_TYPE AS DataType,
                       CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                       c.CHARACTER_MAXIMUM_LENGTH AS MaxLength
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @Table
                ORDER BY c.ORDINAL_POSITION",
            new { Schema = schema, Table = table }, ct: ct);

        var primaryKeys = (await session.QueryAsync<string>(@"
                SELECT k.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
                  ON k.CONSTRAINT_NAME = t.CONSTRAINT_NAME AND k.TABLE_SCHEMA = t.TABLE_SCHEMA
                WHERE t.CONSTRAINT_TYPE = 'PRIMARY KEY'
                  AND t.TABLE_SCHEMA = @Schema AND t.TABLE_NAME = @Table",
            new { Schema = schema, Table = table }, ct: ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Tablodan TEK sorguyla ornek satir kumesi cekilir ve butun kolon
        // istatistikleri bu kume uzerinden bellekte hesaplanir.
        //
        // Onceki surum her kolon icin ayri COUNT(DISTINCT)/MIN/MAX ve ayri bir
        // ornek sorgusu calistiriyordu: 30 kolonlu bir tabloda 60'tan fazla tam
        // tarama demekti ve gateway zaman asimina ugruyordu. Profil icin kesin
        // sayilara ihtiyac yok; amac kolonun ne oldugunu anlamak.
        var sampleRows = await FetchSampleRowsAsync(
            session, schema, table, result.ApproximateRowCount, ct);
        result.SampledRowCount = sampleRows.Count;

        foreach (var column in columns)
        {
            var stats = ComputeStats(sampleRows, column.ColumnName);

            var decision = SensitiveColumnPolicy.Evaluate(
                column.ColumnName, column.DataType, stats.DistinctCount, LowCardinalityThreshold);

            var profile = new ColumnProfile
            {
                ColumnName = column.ColumnName,
                DataType = column.DataType,
                IsNullable = column.IsNullable,
                MaxLength = column.MaxLength,
                IsPrimaryKey = primaryKeys.Contains(column.ColumnName),
                DistinctCount = stats.DistinctCount,
                NullCount = stats.NullCount,
                MinValue = stats.MinValue,
                MaxValue = stats.MaxValue,
                StatsFromSample = true,
                SamplingDecision = decision.Decision.ToString(),
                SamplingNote = decision.Reason
            };

            var maySample = decision.Decision == SensitiveColumnPolicy.Decision.Allowed
                            || (decision.Decision == SensitiveColumnPolicy.Decision.NeedsConsent && consent);

            if (maySample)
                profile.SampleValues = stats.DistinctValues
                    .Where(SensitiveColumnPolicy.IsValueSafe)
                    .Take(SampleSize)
                    .ToList();

            result.Columns.Add(profile);
        }

        return result;
    }

    /// <summary>
    /// Tablodan ornek satirlari ceker.
    /// </summary>
    /// <remarks>
    /// Duz <c>SELECT TOP n</c> tablonun <b>ilk</b> n satirini verir — yani
    /// genellikle en eski kayitlari. Sevkiyat tablosunun ilk 1000 satiri tek
    /// bir yila, tek bir musteriye ait olabiliyor; bu kesitten cikan
    /// "distinct 3" gibi bir sayi kolonu oldugundan cok daha dar gosteriyor ve
    /// ornek degerler de gercek dagilimi temsil etmiyor. Sozlugu bu sinyalden
    /// uretince alan yanlis taniniyor.
    ///
    /// Bu yuzden tablo ornek boyutundan buyukse <c>TABLESAMPLE</c> ile
    /// sayfalara dagilmis bir kesit alinir. TABLESAMPLE gorunumlerde
    /// calismaz ve az sayfali tabloda bos donebilir; iki durumda da eski
    /// davranisa donuluyor — temsili olmayan ornek, ornegin hic olmamasindan
    /// iyidir.
    /// </remarks>
    private async Task<IReadOnlyList<QueryRow>> FetchSampleRowsAsync(
        IDataSourceSession session, string schema, string table, long approximateRowCount, CancellationToken ct)
    {
        var qualified = $"{Quote(schema)}.{Quote(table)}";

        if (approximateRowCount > SampleRowCount)
        {
            // Hedef: ornek boyutunun birkac kati aday satir. Sayfa bazli
            // secim oldugu icin yuzde tam tutmaz, tavan TOP ile konuyor.
            var percent = Math.Clamp(
                (int)Math.Ceiling(SampleRowCount * 3.0 / approximateRowCount * 100), 1, 100);

            try
            {
                var sampled = await session.QueryRowsAsync(
                    $"SELECT TOP {SampleRowCount} * FROM {qualified} TABLESAMPLE SYSTEM ({percent} PERCENT)",
                    timeoutSeconds: SampleTimeoutSeconds, ct: ct);

                if (sampled.Count > 0) return sampled;

                logger.LogDebug(
                    "TABLESAMPLE boş döndü, düz örneklemeye dönülüyor: {Schema}.{Table}", schema, table);
            }
            catch (DataSourceException ex)
            {
                logger.LogDebug(ex,
                    "TABLESAMPLE kullanılamadı, düz örneklemeye dönülüyor: {Schema}.{Table}", schema, table);
            }
        }

        return await session.QueryRowsAsync(
            $"SELECT TOP {SampleRowCount} * FROM {qualified}",
            timeoutSeconds: SampleTimeoutSeconds, ct: ct);
    }

    private static SampleStats ComputeStats(IReadOnlyList<QueryRow> rows, string columnName)
    {
        var stats = new SampleStats();
        if (rows.Count == 0 || !rows[0].Values.ContainsKey(columnName)) return stats;

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        long nullCount = 0;

        // Min/max kolonun KENDI tipiyle karsilastiriliyor.
        //
        // Onceden her deger once metne cevrilip `CompareOrdinal` ile
        // kiyaslaniyordu: sayisal kolonda 100 < 99 cikiyor, tarih kolonunda
        // "01.12.2024" ile "02.03.2019" metin sirasina gore siralaniyordu.
        // Sozlugu ureten model bu araligi kolonun ne oldugunu anlamak icin
        // kullaniyor — bozuk min/max, yanlis taninan alan demek.
        object? min = null, max = null;

        foreach (var row in rows)
        {
            var raw = row[columnName];
            if (raw is null || raw == DBNull.Value) { nullCount++; continue; }

            var text = Format(raw);
            if (text.Length > 200) text = text[..200];
            distinct.Add(text);

            if (raw is not IComparable comparable) continue;

            if (min is null || (min.GetType() == raw.GetType() && comparable.CompareTo(min) < 0)) min = raw;
            if (max is null || (max.GetType() == raw.GetType() && comparable.CompareTo(max) > 0)) max = raw;
        }

        stats.DistinctCount = distinct.Count;
        stats.NullCount = nullCount;
        stats.MinValue = min is null ? null : Format(min);
        stats.MaxValue = max is null ? null : Format(max);
        stats.DistinctValues = distinct.ToList();
        return stats;
    }

    /// <summary>
    /// Degeri LLM'in okuyacagi bicime cevirir. Tarihler ISO yaziliyor:
    /// yerel bicimde "03.04.2026" gun mu ay mi belli olmuyor.
    /// </summary>
    private static string Format(object value) => value switch
    {
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private sealed class SampleStats
    {
        public int? DistinctCount { get; set; }
        public long? NullCount { get; set; }
        public string? MinValue { get; set; }
        public string? MaxValue { get; set; }
        public List<string> DistinctValues { get; set; } = [];
    }


    private static (string Schema, string Table) SplitTableName(string qualified)
    {
        var cleaned = qualified.Replace("[", "").Replace("]", "").Trim();
        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? (parts[0], parts[^1]) : ("dbo", cleaned);
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    private sealed class ColumnRow
    {
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool IsNullable { get; set; }
        public int? MaxLength { get; set; }
    }

    private sealed class ColumnStats
    {
        public int? DistinctCount { get; set; }
        public long? NullCount { get; set; }
        public string? MinValue { get; set; }
        public string? MaxValue { get; set; }
    }
}

public class DatabaseProfile
{
    public string DatabaseName { get; set; } = "";
    public bool SamplingConsentGiven { get; set; }
    public List<TableProfile> Tables { get; set; } = [];
    public List<RelationshipProfile> Relationships { get; set; } = [];
}

public class TableProfile
{
    public string Schema { get; set; } = "";
    public string TableName { get; set; } = "";

    /// <summary>Sistemin her yerinde kullanilan tekil ad: <c>sema.tablo</c>.</summary>
    public string Qualified => $"{Schema}.{TableName}";

    public long ApproximateRowCount { get; set; }

    /// <summary>Istatistiklerin hesaplandigi ornek satir sayisi.</summary>
    public int SampledRowCount { get; set; }

    public List<ColumnProfile> Columns { get; set; } = [];
    public string? Error { get; set; }
}

public class ColumnProfile
{
    public string ColumnName { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public bool IsPrimaryKey { get; set; }
    public int? DistinctCount { get; set; }
    public long? NullCount { get; set; }
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public List<string> SampleValues { get; set; } = [];

    /// <summary>
    /// Istatistikler tam tablodan degil ornek satirlardan hesaplandi.
    /// LLM'in "distinct 12" gibi bir sayiyi kesin gercek sanmamasi icin
    /// profile acikca yaziliyor.
    /// </summary>
    public bool StatsFromSample { get; set; }

    public string SamplingDecision { get; set; } = "";
    public string? SamplingNote { get; set; }
}

/// <summary>
/// Iki tablo arasindaki tek bir baglanti.
///
/// Yon onemli: <see cref="FromTable"/> anahtari TASIYAN taraf,
/// <see cref="ToTable"/> anahtarin AIT oldugu taraf. Bu yonde ilerlemek
/// (cok -> bir) satir sayisini degistirmez; ters yon satirlari cogaltir ve
/// toplulastirmayi bozar. Kardinalite bu yuzden kenarin uzerinde tasiniyor.
/// </summary>
public class RelationshipProfile
{
    public const string ManyToOne = "many-to-one";
    public const string OneToOne = "one-to-one";

    public string FromTable { get; set; } = "";
    public List<string> FromColumns { get; set; } = [];
    public string ToTable { get; set; } = "";
    public List<string> ToColumns { get; set; } = [];

    public string Cardinality { get; set; } = ManyToOne;

    /// <summary>
    /// Eslesmeyen satir mumkun mu. Mumkunse JOIN LEFT olmali; INNER, o
    /// satirlari sessizce dusurup sayilari degistirir.
    /// </summary>
    public bool IsOptional { get; set; } = true;

    /// <summary>
    /// Veritabani kisiti dogruluyor mu (<c>is_not_trusted = 0</c>). Yalnizca
    /// bu ve <see cref="IsOptional"/> false ise INNER JOIN guvenlidir.
    /// </summary>
    public bool IsTrusted { get; set; }

    /// <summary>fk (bildirilmis) | inferred (cikarsanmis).</summary>
    public string Source { get; set; } = "fk";

    /// <summary>high | medium. medium olanlar kullaniciya dogrulatilir.</summary>
    public string Confidence { get; set; } = "high";

    /// <summary>Cikarsanmis kenarlarda deger ortusme orani (0-1).</summary>
    public double? ValueOverlap { get; set; }

    /// <summary>Hedef tabloda kodun okunabilir karsiligini tutan kolon.</summary>
    public string? LabelColumn { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Aday, adlarin TAM esitliginden degil tek harflik yazim toleransindan
    /// dogduysa true. Kaydedilmiyor — yalnizca kesif logunda isaretlenmesi
    /// icin tasiniyor.
    ///
    /// Sebebi: toleransin gercek testi bu semanin kendisi. Uygulandiktan
    /// sonra <em>yeni</em> cikan kenarlarin gozle taranmasi gerekiyor;
    /// isaret olmadan hangilerinin yeni oldugu logdan okunamaz.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool MatchedByTypo { get; set; }
}

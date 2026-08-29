namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>
/// Sozluk uretimini kac cagriya bolecegimizi belirler.
///
/// Neden gerekli: sozluk tek bir LLM cagrisiyla uretiliyordu ve o cagri
/// secilen BUTUN tablolarin BUTUN kolonlarini kapsiyordu. Prompt modelden her
/// tablo ve her kolon icin anlam + es anlamlilar istedigi icin cikti, kolon
/// sayisiyla dogru orantili buyuyor: birkac yuz kolonda cevap token butcesine
/// sigmiyor ve <c>finish_reason=length</c> ile bos donuyor. Butceyi
/// buyutmek bir iki tablo kazandiriyor, sonra ayni duvara toslaniyor —
/// 28 tablo finans semasinda uc bir sayi degil.
///
/// Bolme tablo SAYISINA gore degil, iki butceye gore yapiliyor:
///
///   * Kolon sayisi — CIKTI tarafini sinirlar. Model kolon basina bir sozluk
///     girdisi uretiyor.
///   * Karakter sayisi — GIRDI penceresini sinirlar. Kolon sayisi bunun
///     yalnizca vekil olcusu: sahada 28 tablo 291.577 token uretip Azure'un
///     272.000 sinirini asti, cunku sorun kolon sayisi degil kolon
///     agirligiydi.
///
/// Tek basina butceye sigmayan tablo kolonlarindan bolunuyor. Boylece "istek
/// pencereye sigar mi" sorusu Azure'a sorulmadan once, burada cevaplaniyor.
/// </summary>
public static class DictionaryChunks
{
    /// <summary>
    /// Bir cagriya girecek en fazla kolon sayisi.
    ///
    /// Kolon basina cikti kabaca 60-90 token (ad, anlam, es anlamlilar, rol,
    /// guven). 120 kolon ~10 bin token demek; <c>BuildSchemaDictionaryAsync</c>
    /// tavani olan 16 binin altinda rahat kaliyor ve dusunme adimlarina da yer
    /// birakiyor.
    /// </summary>
    public const int MaxColumnsPerChunk = 120;

    /// <summary>
    /// Bir parcanin PROFIL JSON'unun en fazla kac karakter olabilecegi.
    ///
    /// Kolon sayisi ne kadar yer kaplanacaginin yalnizca VEKIL olcusu. Genis
    /// bir tablonun kolonu, dar bir tablonunkinden bes kat uzun olabiliyor:
    /// uzun kolon adlari, ornek degerler, min/max metinleri. Sahada 28 tablo
    /// 291.577 token uretip Azure'un 272.000 sinirini asti; kolon butcesi bunu
    /// tek basina onleyemez cunku sorun kolon sayisi degil kolon AGIRLIGIYDI.
    ///
    /// <b>Neyi kapsiyor:</b> olcum parcanin TAMAMI uzerinden yapiliyor —
    /// tablolar, kolonlari ve o parcaya dusen iliskiler dahil. Tek tek
    /// tablolari toplamak yeterli degildi; iliskiler de yer kapliyor ve
    /// hesaba girmiyordu.
    ///
    /// <b>Neyi kapsamiyor:</b> prompt sablonunun kendisi ve her cagriya giren
    /// tablo adlari listesi. Bunlar parca sayisindan bagimsiz, sabit bir ek
    /// yuk (birkac bin karakter). 200 bin karakter ~57 bin token eder; o ek
    /// yukle birlikte bile bugunku pencerelerin cok altinda kaliyor. Sayinin
    /// bu kadar dusuk secilmesinin sebebi de bu — sinira yaklasmak degil,
    /// ona hic yaklasmamak.
    ///
    /// Asil bolmeyi hâlâ kolon butcesi yapiyor (o, CIKTI tarafini
    /// sinirliyor); burasi girdi tarafinin emniyeti.
    /// </summary>
    public const int MaxCharsPerChunk = 200_000;

    /// <summary>
    /// Profili, her biri ayri bir LLM cagrisina girecek alt profillere boler.
    ///
    /// Iki butce birden uygulaniyor: kolon sayisi (cikti tarafini sinirlar) ve
    /// karakter sayisi (girdi penceresini). Hangisi once dolarsa parca orada
    /// kapaniyor.
    ///
    /// Tablo ve kolon sirasi korunuyor: ayni secim her calistirmada ayni
    /// parcalari uretsin, boylece bir hata tekrar edilebilir olsun.
    /// </summary>
    /// <param name="measure">
    /// Bir parcanin kac karakter tutacagi — normalde profil JSON'unun uzunlugu.
    /// PARCA uzerinden olculyor, tek tablo uzerinden degil: iliskiler de yer
    /// kapliyor ve tablo tablo toplamak onlari hesaba katmiyordu. Disaridan
    /// veriliyor ki parcalayici serilestirmeye baglanmasin ve test edilebilsin.
    /// </param>
    public static IReadOnlyList<DatabaseProfile> Split(
        DatabaseProfile profile,
        Func<DatabaseProfile, int> measure,
        int maxColumns = MaxColumnsPerChunk,
        int maxChars = MaxCharsPerChunk)
    {
        if (maxColumns < 1) throw new ArgumentOutOfRangeException(nameof(maxColumns));
        if (maxChars < 1) throw new ArgumentOutOfRangeException(nameof(maxChars));

        var chunks = new List<DatabaseProfile>();
        var current = new List<TableProfile>();
        var currentColumns = 0;

        foreach (var table in profile.Tables)
        {
            foreach (var piece in SplitTable(profile, table, measure, maxColumns, maxChars))
            {
                // Aday parca olculuyor: kolon maliyetleri esitsizken tek tek
                // tablolarin toplamini almak yaniltiyordu, ustelik iliskiler
                // o toplama hic girmiyordu.
                var full = currentColumns + piece.Columns.Count > maxColumns
                           || (current.Count > 0
                               && measure(Build(profile, [.. current, piece])) > maxChars);

                if (current.Count > 0 && full)
                {
                    chunks.Add(Build(profile, current));
                    current = [];
                    currentColumns = 0;
                }

                current.Add(piece);
                currentColumns += piece.Columns.Count;
            }
        }

        if (current.Count > 0) chunks.Add(Build(profile, current));

        return chunks;
    }

    /// <summary>
    /// Tek basina butceye sigmayan tabloyu kolonlarindan boler.
    ///
    /// Onceden bolunmuyordu: tablo kendi parcasina konup geciliyor, sigmazsa
    /// <c>LlmClient</c>'in butce merdivenine birakiliyordu. Tablo basina yetmis
    /// kolonun normal oldugu bir semada bu yeterli degil — uc yuz kolonluk
    /// tek bir tablo butun analizi dusurur.
    ///
    /// Bolunen tablonun sozluk girdisi birden fazla parcada uretilir; bu bir
    /// sorun degil, <c>SchemaDictionaryMerge</c> tablolari ada gore
    /// tekillestiriyor ve kolonlari (tablo, kolon) ciftine gore birlestiriyor.
    /// Modele de kolonlarin bir kismini gordugu soyleniyor.
    /// </summary>
    private static IEnumerable<TableProfile> SplitTable(
        DatabaseProfile profile, TableProfile table,
        Func<DatabaseProfile, int> measure, int maxColumns, int maxChars)
    {
        var columnCount = table.Columns.Count;
        var whole = measure(Build(profile, [table]));

        if (columnCount <= maxColumns && whole <= maxChars)
        {
            yield return table;
            yield break;
        }

        if (columnCount <= 1)
        {
            // Bolunecek bir sey kalmadi: tek kolonu olan (ya da hic olmayan)
            // bir tabloyu daha fazla kucultemeyiz. Tek basina butceyi asan bir
            // kolon kalirsa cagri yine buyuk olur; onun karsiligi
            // LlmClient'taki butce merdiveni.
            yield return table;
            yield break;
        }

        // Ilk tahmin kolon basina ORTALAMA maliyetten cikiyor. Ortalama tek
        // basina yeterli degil: bir tablonun tek bir kolonu (uzun ornek
        // degerleri olan bir kod kolonu gibi) digerlerinin yuz kati
        // olabiliyor ve ortalama o dilimi butcenin uzerine cikariyor. Bu
        // yuzden her dilim ayrica OLCULUYOR ve sigana kadar yariya
        // indiriliyor — tahmin yalnizca kac olcum yapacagimizi belirliyor,
        // butceyi degil.
        var perColumn = Math.Max(1, whole / columnCount);
        var guess = Math.Min(maxColumns, Math.Max(1, maxChars / perColumn));

        var start = 0;
        while (start < columnCount)
        {
            var take = Math.Min(guess, columnCount - start);
            TableProfile piece;

            while (true)
            {
                piece = Slice(table, table.Columns.Skip(start).Take(take).ToList());
                if (take <= 1 || measure(Build(profile, [piece])) <= maxChars) break;
                take = Math.Max(1, take / 2);
            }

            yield return piece;
            start += take;
        }
    }

    /// <summary>Ayni tablonun, kolonlarinin bir kismini tasiyan kopyasi.</summary>
    private static TableProfile Slice(TableProfile table, List<ColumnProfile> columns) => new()
    {
        Schema = table.Schema,
        TableName = table.TableName,
        ApproximateRowCount = table.ApproximateRowCount,
        SampledRowCount = table.SampledRowCount,
        Error = table.Error,
        Columns = columns
    };

    /// <summary>
    /// Alt profil. Iliskilerden yalnizca bu parcanin tablolarina DEGEN olanlar
    /// tasiniyor: sozluk kurallarindan biri modelden her tablonun
    /// <c>relatedTables</c> alanini doldurmasini istiyor ve bunu iliski
    /// kaydina bakarak yapiyor. Butun iliskileri her parcaya koymak ise
    /// kazanilan yeri geri verirdi.
    ///
    /// Iliskilerin kendisi zaten modelden gecmiyor; olculmus halleriyle
    /// sozluge sonradan ekleniyor (bkz. <c>AttachProfileFacts</c>). Burada
    /// tasinmalarinin tek sebebi modele baglam vermek.
    /// </summary>
    private static DatabaseProfile Build(DatabaseProfile source, List<TableProfile> tables)
    {
        var names = tables.Select(t => t.Qualified).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new DatabaseProfile
        {
            DatabaseName = source.DatabaseName,
            SamplingConsentGiven = source.SamplingConsentGiven,
            Tables = tables,
            Relationships = source.Relationships
                .Where(r => names.Contains(r.FromTable) || names.Contains(r.ToTable))
                .ToList()
        };
    }
}

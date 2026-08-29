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
/// Bolme KOLON butcesine gore yapiliyor, tablo sayisina gore degil: iki yuz
/// kolonluk tek bir tablo, bes kolonluk on tablodan daha agir.
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
    /// Profili, her biri ayri bir LLM cagrisina girecek alt profillere boler.
    ///
    /// Tablo sirasi korunuyor: ayni secim her calistirmada ayni parcalari
    /// uretsin, boylece bir hata tekrar edilebilir olsun.
    ///
    /// Tek bir tablo tek basina butceyi asiyorsa yine de kendi parcasinda
    /// gonderiliyor — kolonlari cagrilar arasinda bolmek, tablonun sozluk
    /// girdisini birden fazla kez urettirmek ve hangisinin gecerli oldugunu
    /// belirsiz birakmak olurdu. O durumda <c>LlmClient</c>'in butce
    /// buyutme merdiveni son savunma olarak kaliyor.
    /// </summary>
    public static IReadOnlyList<DatabaseProfile> Split(
        DatabaseProfile profile, int maxColumns = MaxColumnsPerChunk)
    {
        if (maxColumns < 1)
            throw new ArgumentOutOfRangeException(nameof(maxColumns));

        var chunks = new List<DatabaseProfile>();
        var current = new List<TableProfile>();
        var currentColumns = 0;

        foreach (var table in profile.Tables)
        {
            var columns = table.Columns.Count;

            if (current.Count > 0 && currentColumns + columns > maxColumns)
            {
                chunks.Add(Build(profile, current));
                current = [];
                currentColumns = 0;
            }

            current.Add(table);
            currentColumns += columns;
        }

        if (current.Count > 0) chunks.Add(Build(profile, current));

        return chunks;
    }

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

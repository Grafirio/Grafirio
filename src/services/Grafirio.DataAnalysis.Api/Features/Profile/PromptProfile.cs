using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>
/// Profilin MODELE GONDERILEN hali.
///
/// Profil nesnesi iki ayri isi birden goruyordu: sozluk cagrisinin girdisi
/// olmak ve olculmus gercekleri (iliskiler, kod degerleri) tasimak. Ikisi ayni
/// JSON'dan beslendigi icin modele, karar verirken hicbir ise yaramayan
/// alanlar da gidiyordu — kolon basina bir cumlelik ornekleme gerekcesi, her
/// satirda tekrar eden bir bayrak, yirmi ornek deger.
///
/// Burasi yalnizca birincisini uretiyor. Profil nesnesinin kendisine
/// dokunulmuyor: <c>codeValues</c> ve <c>profileStats</c> hâlâ tam
/// <see cref="ColumnProfile.SampleValues"/> listesini okuyor.
///
/// Bunun sozluk tavaniyla ilgisi yok — o tavan CIKTI tarafinda ve cozumu
/// <see cref="DictionaryChunks"/>. Buradaki kazanc maliyet, sure ve baglam
/// penceresine karsi pay.
/// </summary>
public static class PromptProfile
{
    /// <summary>
    /// Kolon basina modele gosterilen en fazla ornek deger.
    ///
    /// Profil yirmi tane topluyor ve <c>codeValues</c> hepsini kullaniyor —
    /// bir kod kolonunun butun degerlerini bilmek sorgu aninda gerekli. Ama
    /// modelin "bu kolon ne" sorusuna cevap vermesi icin sekiz deger de
    /// yirmi kadar bilgi veriyor.
    /// </summary>
    public const int MaxSampleValues = 8;

    public static string Serialize(DatabaseProfile profile, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(new
        {
            profile.DatabaseName,
            profile.SamplingConsentGiven,
            Tables = profile.Tables.Select(Project),
            profile.Relationships
        }, options);

    /// <summary>
    /// Bir tablonun prompt'ta kaplayacagi karakter sayisi.
    ///
    /// Parcalayici bunu kullaniyor. Kolon SAYISI, bir parcanin ne kadar yer
    /// kaplayacaginin yalnizca vekil olcusu: kolonun tipi, ornek degerleri ve
    /// istatistikleri bes kat fark yaratabiliyor. Gercek olcuyu tahmin etmek
    /// yerine olcmek, "istek pencereye sigar mi" sorusunu Azure'a sormadan
    /// once cevaplamanin tek yolu.
    /// </summary>
    public static int EstimateChars(TableProfile table, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(Project(table), options).Length;

    /// <summary>Tek bir tablonun modele gonderilen bicimi.</summary>
    private static object Project(TableProfile table) =>
        new
        {
                table.Schema,
                table.TableName,
                // Modelin sozlukteki `tables[].name` alanina yazdigi ad bu.
                table.Qualified,
                table.ApproximateRowCount,
                table.SampledRowCount,
                table.Error,
                Columns = table.Columns.Select(column => new
                {
                    column.ColumnName,
                    column.DataType,
                    column.IsNullable,
                    column.MaxLength,
                    // Yalnizca dogruyken yaziliyor: sekiz yuz kolonda
                    // "isPrimaryKey": false satiri bilgi degil, gurultu.
                    IsPrimaryKey = column.IsPrimaryKey ? true : (bool?)null,
                    column.DistinctCount,
                    column.NullCount,
                    column.MinValue,
                    column.MaxValue,
                    // Bos liste yerine hic yazilmiyor. "Ornek alinamadi"
                    // bilgisi prompt'ta bir kez anlatiliyor; kolon basina
                    // tekrar etmenin karsiligi yok.
                    SampleValues = column.SampleValues.Count == 0
                        ? null
                        : column.SampleValues.Take(MaxSampleValues).ToList()
                })
        };
}

using Grafirio.DataAnalysis.Api.Features.Profile;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Sozluk uretiminin kac cagriya bolunecegi.
///
/// Bolme kolon butcesine gore yapiliyor, tablo sayisina gore degil: cikti
/// token'lari kolon basina harcaniyor ve iki yuz kolonluk tek bir tablo, bes
/// kolonluk on tablodan agir. Butce yanlis hesaplanirsa parca yine tavana
/// takilir ve bolmenin bir anlami kalmaz.
/// </summary>
public class DictionaryChunksTests
{
    private static TableProfile Table(string name, int columns) => new()
    {
        Schema = "dbo",
        TableName = name,
        Columns = Enumerable.Range(1, columns)
            .Select(i => new ColumnProfile { ColumnName = $"C{i}", DataType = "int" })
            .ToList()
    };

    private static DatabaseProfile Profile(params TableProfile[] tables) => new()
    {
        DatabaseName = "Test",
        Tables = tables.ToList()
    };

    private static List<List<string>> Layout(IReadOnlyList<DatabaseProfile> chunks) =>
        chunks.Select(c => c.Tables.Select(t => t.TableName).ToList()).ToList();

    [Fact]
    public void Kucuk_sema_bolunmuyor()
    {
        // Tek cagriya sigan semada davranis degismemeli: prompt da, uretilen
        // sozluk de eskisiyle ayni kalsin.
        var chunks = DictionaryChunks.Split(Profile(Table("A", 10), Table("B", 10)));

        Assert.Single(chunks);
        Assert.Equal(2, chunks[0].Tables.Count);
    }

    [Fact]
    public void Kolon_butcesi_asilinca_yeni_parca_aciliyor()
    {
        var chunks = DictionaryChunks.Split(
            Profile(Table("A", 40), Table("B", 40), Table("C", 40), Table("D", 40)),
            maxColumns: 100);

        Assert.Equal([["A", "B"], ["C", "D"]], Layout(chunks));
    }

    [Fact]
    public void Tablo_sirasi_korunuyor()
    {
        // Ayni secim her calistirmada ayni parcalari uretmeli; yoksa bir hata
        // tekrar edilebilir olmaz.
        var chunks = DictionaryChunks.Split(
            Profile(Table("Z", 60), Table("A", 60), Table("M", 60)), maxColumns: 100);

        Assert.Equal([["Z"], ["A"], ["M"]], Layout(chunks));
    }

    [Fact]
    public void Tek_basina_butceyi_asan_tablo_kendi_parcasinda_gidiyor()
    {
        // Kolonlari cagrilar arasinda bolmek, ayni tablonun sozluk girdisini
        // birden fazla kez urettirmek ve hangisinin gecerli oldugunu belirsiz
        // birakmak olurdu.
        var chunks = DictionaryChunks.Split(
            Profile(Table("Kucuk", 10), Table("Dev", 500), Table("Diger", 10)),
            maxColumns: 100);

        Assert.Equal([["Kucuk"], ["Dev"], ["Diger"]], Layout(chunks));
    }

    [Fact]
    public void Iliskilerden_yalnizca_parcaya_degenler_tasiniyor()
    {
        // Sozluk kurallarindan biri modelden `relatedTables` alanini
        // doldurmasini istiyor ve bunu iliski kaydina bakarak yapiyor. Butun
        // iliskileri her parcaya koymak, bolmeyle kazanilan yeri geri verirdi.
        var profile = Profile(Table("A", 60), Table("B", 60));
        profile.Relationships =
        [
            new RelationshipProfile { FromTable = "dbo.A", ToTable = "dbo.X" },
            new RelationshipProfile { FromTable = "dbo.Y", ToTable = "dbo.B" },
            new RelationshipProfile { FromTable = "dbo.P", ToTable = "dbo.Q" },
        ];

        var chunks = DictionaryChunks.Split(profile, maxColumns: 100);

        Assert.Equal([["A"], ["B"]], Layout(chunks));
        Assert.Equal(["dbo.X"], chunks[0].Relationships.Select(r => r.ToTable));
        Assert.Equal(["dbo.Y"], chunks[1].Relationships.Select(r => r.FromTable));
    }

    [Fact]
    public void Bos_profil_parca_uretmiyor() =>
        Assert.Empty(DictionaryChunks.Split(Profile()));

    [Fact]
    public void Gecersiz_butce_reddediliyor() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DictionaryChunks.Split(Profile(Table("A", 1)), maxColumns: 0));
}

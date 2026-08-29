using Grafirio.DataAnalysis.Api.Features.Profile;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Sozluk uretiminin kac cagriya bolunecegi.
///
/// Iki butce birden isliyor ve ikisi de ayri bir sinira karsi duruyor: kolon
/// sayisi CIKTI tavanina, karakter sayisi GIRDI penceresine. Sahada once
/// birincisi ("token butcesine sigmadi"), sonra ikincisi
/// ("context_length_exceeded") patladi — yani ikisi de kuramsal degil.
///
/// Butce yanlis hesaplanirsa parca yine tavana takilir ve bolmenin bir anlami
/// kalmaz; hesap dogru ama bolme yanlis olursa tablolar sozlukten sessizce
/// duser. Ikisi de gozle gorulmeyen turden.
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

    /// <summary>Kolon basina sabit maliyet varsayan olcum.</summary>
    private static Func<DatabaseProfile, int> Cost(int perColumn) =>
        chunk => chunk.Tables.Sum(t => t.Columns.Count) * perColumn;

    /// <summary>
    /// Esitsiz maliyet: adi AGIR ile baslayan kolon otekilerin yuz kati.
    ///
    /// Gercek semada oluyor — uzun ornek degerleri olan bir kod kolonu,
    /// yanindaki int kolonundan cok daha fazla yer kapliyor. Ortalamaya gore
    /// dilimlemek boyle bir tabloda butceyi asiyordu.
    /// </summary>
    private static readonly Func<DatabaseProfile, int> Uneven = chunk =>
        chunk.Tables.Sum(t => t.Columns.Sum(
            c => c.ColumnName.StartsWith("AGIR", StringComparison.Ordinal) ? 1000 : 10));

    /// <summary>Boyut butcesini devre disi birakan olcum.</summary>
    private static readonly Func<DatabaseProfile, int> Free = _ => 0;

    [Fact]
    public void Kucuk_sema_bolunmuyor()
    {
        // Tek cagriya sigan semada davranis degismemeli: prompt da, uretilen
        // sozluk de eskisiyle ayni kalsin.
        var chunks = DictionaryChunks.Split(Profile(Table("A", 10), Table("B", 10)), Free);

        Assert.Single(chunks);
        Assert.Equal(2, chunks[0].Tables.Count);
    }

    [Fact]
    public void Kolon_butcesi_asilinca_yeni_parca_aciliyor()
    {
        var chunks = DictionaryChunks.Split(
            Profile(Table("A", 40), Table("B", 40), Table("C", 40), Table("D", 40)),
            Free, maxColumns: 100);

        Assert.Equal([["A", "B"], ["C", "D"]], Layout(chunks));
    }

    [Fact]
    public void Karakter_butcesi_kolon_butcesinden_once_dolabiliyor()
    {
        // Asil vaka bu: kolon sayisi bolca yer birakiyor ama kolonlar agir.
        // Sahada 28 tablo 291.577 token uretti; kolon butcesi tek basina
        // bunu goremezdi cunku sorun kolon sayisi degil kolon agirligiydi.
        var chunks = DictionaryChunks.Split(
            Profile(Table("A", 40), Table("B", 40), Table("C", 40), Table("D", 40)),
            Cost(100), maxColumns: 1000, maxChars: 9000);

        Assert.Equal([["A", "B"], ["C", "D"]], Layout(chunks));
    }

    [Fact]
    public void Tablo_sirasi_korunuyor()
    {
        // Ayni secim her calistirmada ayni parcalari uretmeli; yoksa bir hata
        // tekrar edilebilir olmaz.
        var chunks = DictionaryChunks.Split(
            Profile(Table("Z", 60), Table("A", 60), Table("M", 60)), Free, maxColumns: 100);

        Assert.Equal([["Z"], ["A"], ["M"]], Layout(chunks));
    }

    [Fact]
    public void Tek_basina_butceyi_asan_tablo_kolonlarindan_bolunuyor()
    {
        // Onceden bolunmuyordu: tablo kendi parcasina konup geciliyordu ve
        // sigmazsa butun analiz dusuyordu. Tablo basina yetmis kolonun normal
        // oldugu bir semada bu yeterli degil.
        var chunks = DictionaryChunks.Split(
            Profile(Table("Kucuk", 10), Table("Dev", 250), Table("Diger", 10)),
            Free, maxColumns: 100);

        // 250 kolon 100 + 100 + 50'ye bolunuyor; artan 50'lik parca bos yer
        // biraktigi icin sonraki tablo onunla ayni cagriya giriyor. Her
        // parcanin ayri cagri olmasi gereksiz cagri demek olurdu.
        Assert.Equal(
            [["Kucuk"], ["Dev"], ["Dev"], ["Dev", "Diger"]],
            Layout(chunks));
    }

    [Fact]
    public void Agir_tablo_karakter_butcesine_gore_bolunuyor()
    {
        var chunks = DictionaryChunks.Split(
            Profile(Table("Dev", 100)), Cost(100), maxColumns: 1000, maxChars: 3000);

        // Kolon basina 100 karakter, parca basina 3000 → 30'ar kolon.
        Assert.Equal([30, 30, 30, 10], chunks.Select(c => c.Tables[0].Columns.Count));
    }

    [Fact]
    public void Kolon_maliyetleri_esitsizken_de_butce_asilmiyor()
    {
        // Inceleme bulgusu: dilim boyutu kolon basina ORTALAMA maliyetten
        // hesaplaniyordu. Tek bir agir kolon ortalamayi dusuk gosterip dilimi
        // butcenin uzerine cikariyor, "sert" sanilan sinir fiilen
        // uygulanmiyordu — Azure yine context_length_exceeded dondururdu.
        var table = Table("Karisik", 12);
        table.Columns[0].ColumnName = "AGIR_OrnekDegerliKolon";
        table.Columns[6].ColumnName = "AGIR_IkinciAgirKolon";

        var chunks = DictionaryChunks.Split(
            Profile(table), Uneven, maxColumns: 100, maxChars: 500);

        // Tek kolonluk parcalar disinda hicbir parca butceyi asmamali; tek
        // kolon daha fazla bolunemez ve bu bilincli sinir.
        Assert.All(
            chunks.Where(c => c.Tables.Sum(t => t.Columns.Count) > 1),
            c => Assert.True(Uneven(c) <= 500, $"parça {Uneven(c)} karakter, bütçe 500"));

        // Hicbir kolon dusmemeli.
        Assert.Equal(12, chunks.Sum(c => c.Tables.Sum(t => t.Columns.Count)));
    }

    [Fact]
    public void Bolunen_tablonun_kimligi_ve_kolon_sirasi_korunuyor()
    {
        // Parcalar birlestirmede tablo ADINA gore tekillestiriliyor; kimlik
        // kaybolursa ayni tablo iki ayri tablo gibi gorunur.
        var chunks = DictionaryChunks.Split(Profile(Table("Dev", 250)), Free, maxColumns: 100);

        Assert.All(chunks, c => Assert.Equal("dbo.Dev", c.Tables[0].Qualified));
        Assert.Equal(
            Enumerable.Range(1, 250).Select(i => $"C{i}"),
            chunks.SelectMany(c => c.Tables[0].Columns.Select(col => col.ColumnName)));
    }

    [Fact]
    public void Tek_kolonlu_tablo_daha_fazla_bolunmuyor()
    {
        // Bolunecek bir sey kalmadiginda sonsuz donguye girmemeli; butce
        // asilsa bile tablo oldugu gibi gonderiliyor.
        var chunks = DictionaryChunks.Split(
            Profile(Table("Tek", 1)), Cost(10_000), maxColumns: 100, maxChars: 10);

        Assert.Single(chunks);
        Assert.Single(chunks[0].Tables[0].Columns);
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

        var chunks = DictionaryChunks.Split(profile, Free, maxColumns: 100);

        Assert.Equal([["A"], ["B"]], Layout(chunks));
        Assert.Equal(["dbo.X"], chunks[0].Relationships.Select(r => r.ToTable));
        Assert.Equal(["dbo.Y"], chunks[1].Relationships.Select(r => r.FromTable));
    }

    [Fact]
    public void Bos_profil_parca_uretmiyor() =>
        Assert.Empty(DictionaryChunks.Split(Profile(), Free));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void Gecersiz_butce_reddediliyor(int maxColumns, int maxChars) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DictionaryChunks.Split(Profile(Table("A", 1)), Free, maxColumns, maxChars));
}

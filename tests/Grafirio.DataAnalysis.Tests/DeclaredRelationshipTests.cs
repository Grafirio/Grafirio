using Grafirio.DataAnalysis.Api.Features.Profile;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Kullanicinin beyan ettigi baglantilarin aday kenara cevrilmesi.
///
/// Bu, ad kalibinin hic bulamayacagi iliskilerin sisteme girdigi tek kapi —
/// <c>F1</c>, <c>X_REF</c>, kisaltmalar. Kapinin genis olmasi gerekiyor ama
/// emniyet kilidi kalmali: benzersiz olmayan bir hedefe join satirlari
/// cogaltir ve <c>COUNT(*)</c> sessizce baska bir seyi saymaya baslar.
/// Kullanici bir iliskinin VARLIGINI bilebilir, toplamlari sisirip
/// sisirmeyecegini bilemez.
/// </summary>
public class DeclaredRelationshipTests
{
    private static readonly RelationshipDiscovery Discovery =
        new(NullLogger<RelationshipDiscovery>.Instance);

    private static TableProfile Table(string name, params (string Column, string Type, bool Key)[] columns)
    {
        var table = new TableProfile { Schema = "dbo", TableName = name };
        foreach (var (column, type, key) in columns)
            table.Columns.Add(new ColumnProfile { ColumnName = column, DataType = type, IsPrimaryKey = key });
        return table;
    }

    private static Dictionary<string, HashSet<string>> KeysOf(params TableProfile[] tables) =>
        tables.ToDictionary(
            t => t.Qualified,
            t => t.Columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName)
                  .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Adi_hic_benzemeyen_kolonlar_beyanla_baglanir()
    {
        // Ad kalibinin coz(e)medigi sinif: "F1" ile "Id" arasinda hicbir
        // benzerlik yok. Kullanici biliyor, sistem bilemez.
        var hareket = Table("Hareketler", ("Id", "int", true), ("F1", "int", false));
        var cari = Table("Cariler", ("Id", "int", true));

        var edge = Assert.Single(Discovery.BuildDeclaredCandidates(
            [new RelationshipDiscovery.DeclaredLink("dbo.Hareketler", "F1", "dbo.Cariler", "Id")],
            [hareket, cari], KeysOf(hareket, cari)));

        Assert.Equal("dbo.Hareketler", edge.FromTable);
        Assert.Equal("F1", edge.FromColumns[0]);
        Assert.Equal("dbo.Cariler", edge.ToTable);
        Assert.Equal("Id", edge.ToColumns[0]);
        Assert.Equal("declared", edge.Source);
        // Beyanda da referans butunlugu garantisi yok: LEFT JOIN.
        Assert.True(edge.IsOptional);
        Assert.False(edge.IsTrusted);
    }

    [Fact]
    public void Metadata_olmayan_hedef_veri_dogrulamasi_olmadan_kabul_edilmez()
    {
        // The synchronous candidate builder cannot establish uniqueness from data.
        var hareket = Table("Hareketler", ("Id", "int", true), ("CariKodu", "nvarchar", false));
        var cari = Table("Cariler", ("Kod", "nvarchar", false), ("Ad", "nvarchar", false));

        Assert.Empty(Discovery.BuildDeclaredCandidates(
            [new RelationshipDiscovery.DeclaredLink("dbo.Hareketler", "CariKodu", "dbo.Cariler", "Kod")],
            [hareket, cari], KeysOf(hareket, cari)));
    }

    [Fact]
    public void Cocuk_kolon_da_benzersizse_bire_bir_olur()
    {
        var profil = Table("Profiller", ("KullaniciId", "int", true));
        var kullanici = Table("Kullanicilar", ("Id", "int", true));

        var edge = Assert.Single(Discovery.BuildDeclaredCandidates(
            [new RelationshipDiscovery.DeclaredLink("dbo.Profiller", "KullaniciId", "dbo.Kullanicilar", "Id")],
            [profil, kullanici], KeysOf(profil, kullanici)));

        Assert.Equal(RelationshipProfile.OneToOne, edge.Cardinality);
    }

    [Fact]
    public void Buyuk_kucuk_harf_ve_koseli_parantez_onemsiz()
    {
        // Beyan kullanicidan ya da eski bir kayittan geliyor; yazimi
        // semadakiyle birebir ayni olmayabilir.
        var hareket = Table("Hareketler", ("Id", "int", true), ("F1", "int", false));
        var cari = Table("Cariler", ("Id", "int", true));

        var edge = Assert.Single(Discovery.BuildDeclaredCandidates(
            [new RelationshipDiscovery.DeclaredLink("DBO.HAREKETLER", "f1", "dbo.cariler", "ID")],
            [hareket, cari], KeysOf(hareket, cari)));

        // Cikti semadaki yazimi tasiyor, kullanicinin yazdigini degil:
        // uretilen SQL semayla eslesmek zorunda.
        Assert.Equal("F1", edge.FromColumns[0]);
        Assert.Equal("Id", edge.ToColumns[0]);
    }

    [Theory]
    [InlineData("dbo.BoyleBirTabloYok", "F1", "dbo.Cariler", "Id")]   // tablo yok
    [InlineData("dbo.Hareketler", "BoyleBirKolonYok", "dbo.Cariler", "Id")] // kolon yok
    [InlineData("dbo.Hareketler", "F1", "dbo.Hareketler", "Id")]      // kendi tablosu
    public void Cozulemeyen_beyan_kenar_uretmez(
        string fromTable, string fromColumn, string toTable, string toColumn)
    {
        var hareket = Table("Hareketler", ("Id", "int", true), ("F1", "int", false));
        var cari = Table("Cariler", ("Id", "int", true));

        Assert.Empty(Discovery.BuildDeclaredCandidates(
            [new RelationshipDiscovery.DeclaredLink(fromTable, fromColumn, toTable, toColumn)],
            [hareket, cari], KeysOf(hareket, cari)));
    }

    /* ── Geçersizleşen beyan sessizce düşmez ──────────────────────────────
       Şema değişir: kolon kaldırılır, tablo seçimden çıkar, bir zamanlar
       benzersiz olan anahtar çoğullaşır. Kullanıcı bunu görmezse kurduğu
       bağlantının hâlâ çalıştığını sanar ve sorgu "bu tabloları
       birleştiremem" dediğinde sebebi hiçbir yerde yazmaz. */

    [Fact]
    public void Kurulamayan_beyanin_sebebi_disari_veriliyor()
    {
        var hareket = Table("Hareketler", ("Id", "int", true), ("CariKodu", "nvarchar", false));
        var cari = Table("Cariler", ("Kod", "nvarchar", false));
        var problems = new List<RelationshipDiscovery.DeclaredProblem>();

        Discovery.BuildDeclaredCandidates(
            [new RelationshipDiscovery.DeclaredLink("dbo.Hareketler", "CariKodu", "dbo.Cariler", "Kod")],
            [hareket, cari], KeysOf(hareket, cari), problems);

        var problem = Assert.Single(problems);
        Assert.Equal("dbo.Hareketler", problem.Link.FromTable);
        Assert.Contains("benzersizliği doğrulanamadı", problem.Reason);
        Assert.DoesNotContain("benzersiz değil", problem.Reason);
    }

    [Theory]
    [InlineData("dbo.BoyleBirTabloYok", "F1", "dbo.Cariler", "Id", "tablosu")]
    [InlineData("dbo.Hareketler", "YokKolon", "dbo.Cariler", "Id", "kolonu artık yok")]
    [InlineData("dbo.Hareketler", "F1", "dbo.Hareketler", "Id", "kendisini")]
    public void Her_eleme_sebebiyle_birlikte_bildiriliyor(
        string fromTable, string fromColumn, string toTable, string toColumn, string fragment)
    {
        var hareket = Table("Hareketler", ("Id", "int", true), ("F1", "int", false));
        var cari = Table("Cariler", ("Id", "int", true));
        var problems = new List<RelationshipDiscovery.DeclaredProblem>();

        Discovery.BuildDeclaredCandidates(
            [new RelationshipDiscovery.DeclaredLink(fromTable, fromColumn, toTable, toColumn)],
            [hareket, cari], KeysOf(hareket, cari), problems);

        Assert.Contains(fragment, Assert.Single(problems).Reason);
    }

    [Fact]
    public void Kurulan_beyan_sorun_bildirmiyor()
    {
        // İkinci yarı birincisi kadar önemli: düzelen bir sorunun ekranda
        // asılı kalması, kullanıcıyı olmayan bir sorunu kovalamaya gönderir.
        var hareket = Table("Hareketler", ("Id", "int", true), ("F1", "int", false));
        var cari = Table("Cariler", ("Id", "int", true));
        var problems = new List<RelationshipDiscovery.DeclaredProblem>();

        Discovery.BuildDeclaredCandidates(
            [new RelationshipDiscovery.DeclaredLink("dbo.Hareketler", "F1", "dbo.Cariler", "Id")],
            [hareket, cari], KeysOf(hareket, cari), problems);

        Assert.Empty(problems);
    }

    [Fact]
    public void Beyan_yoksa_hicbir_sey_uretilmiyor()
    {
        var hareket = Table("Hareketler", ("Id", "int", true));

        Assert.Empty(Discovery.BuildDeclaredCandidates(null, [hareket], KeysOf(hareket)));
        Assert.Empty(Discovery.BuildDeclaredCandidates([], [hareket], KeysOf(hareket)));
    }
}

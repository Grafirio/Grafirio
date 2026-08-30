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
    public void Benzersiz_olmayan_hedef_beyanda_da_reddedilir()
    {
        // Emniyet kilidi. Kullanici ne kadar emin olursa olsun, benzersiz
        // olmayan bir hedefe baglanmak satirlari cogaltir ve butun toplamlar
        // sessizce sisir. Bu, kullanicinin bilebilecegi bir sey degil.
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

    [Fact]
    public void Beyan_yoksa_hicbir_sey_uretilmiyor()
    {
        var hareket = Table("Hareketler", ("Id", "int", true));

        Assert.Empty(Discovery.BuildDeclaredCandidates(null, [hareket], KeysOf(hareket)));
        Assert.Empty(Discovery.BuildDeclaredCandidates([], [hareket], KeysOf(hareket)));
    }
}

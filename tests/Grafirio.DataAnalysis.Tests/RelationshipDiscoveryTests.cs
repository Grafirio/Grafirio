using Grafirio.DataAnalysis.Api.Features.Profile;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Aday baglanti uretimi — veritabanina dokunmayan kismi.
///
/// Uretilen her sey ADAYDIR; gercek kosumda deger ortusmesi bunlari ayrica
/// eliyor. Burada olculen sey, dogru adaylarin uretilip yanlislarin
/// uretilmemesi.
/// </summary>
public class RelationshipDiscoveryTests
{
    private static TableProfile Table(string name, params (string Column, string Type, bool Key)[] columns)
    {
        var table = new TableProfile { Schema = "dbo", TableName = name };
        foreach (var (column, type, key) in columns)
            table.Columns.Add(new ColumnProfile
            {
                ColumnName = column,
                DataType = type,
                IsPrimaryKey = key,
            });
        return table;
    }

    private static Dictionary<string, HashSet<string>> KeysOf(params TableProfile[] tables) =>
        tables.ToDictionary(
            t => t.Qualified,
            t => t.Columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName)
                  .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Rol_onekli_iki_kolon_ayri_kenar_uretir()
    {
        // "firma" tek basina yetmiyor: gonderici ve alici ayri anlamlar.
        var shipments = Table("Shipments",
            ("Id", "int", true),
            ("SenderCompanyId", "int", false),
            ("ReceiverCompanyId", "int", false));
        var companies = Table("Companies", ("Id", "int", true), ("Unvan", "nvarchar", false));

        var edges = RelationshipDiscovery
            .BuildCandidates([shipments, companies], KeysOf(shipments, companies))
            .ToList();

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal("dbo.Companies", e.ToTable));
        Assert.Contains(edges, e => e.FromColumns[0] == "SenderCompanyId");
        Assert.Contains(edges, e => e.FromColumns[0] == "ReceiverCompanyId");
    }

    [Fact]
    public void Turkce_cogul_ve_kod_anahtari_cozulur()
    {
        var siparis = Table("Siparisler", ("No", "nvarchar", true), ("MusteriKodu", "nvarchar", false));
        var musteri = Table("Musteriler", ("Kod", "nvarchar", true), ("Adi", "nvarchar", false));

        var edge = Assert.Single(RelationshipDiscovery
            .BuildCandidates([siparis, musteri], KeysOf(siparis, musteri)));

        Assert.Equal("dbo.Musteriler", edge.ToTable);
        Assert.Equal("Kod", edge.ToColumns[0]);
        Assert.Equal(RelationshipProfile.ManyToOne, edge.Cardinality);
        // Cikarsanmis kenar guvenilir sayilmaz: JOIN her zaman LEFT olmali.
        Assert.True(edge.IsOptional);
        Assert.False(edge.IsTrusted);
        Assert.Equal("inferred", edge.Source);
    }

    [Fact]
    public void Sistem_onekli_tablolar_arasindaki_bag_bulunur()
    {
        // Gercek musteri semasi (TechLogistics). Onek cozulmeden bu bag
        // bulunamiyordu — kuru kosum 0 aday donuyordu.
        var reference = Table("L_INT_ExportReference", ("ReferenceId", "int", true));
        var position = Table("L_ROD_ExportPosition", ("PositionId", "int", true));
        var cross = Table("L_INT_ExportReferenceCross",
            ("ExportCrossId", "int", true),
            ("ReferenceId", "int", false),
            ("PositionId", "int", false));

        var edges = RelationshipDiscovery
            .BuildCandidates([reference, position, cross], KeysOf(reference, position, cross))
            .ToList();

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal("dbo.L_INT_ExportReferenceCross", e.FromTable));
        Assert.Contains(edges, e => e.ToTable == "dbo.L_INT_ExportReference");
        Assert.Contains(edges, e => e.ToTable == "dbo.L_ROD_ExportPosition");
    }

    [Fact]
    public void Tablonun_kendi_anahtari_kenar_uretmez()
    {
        // ReferenceId -> ReferenceId ve ReferenceNo -> ReferenceId uretiliyordu:
        // ikisi de ayni satirin kimligi, aralarinda gidilecek yol yok.
        var reference = Table("L_INT_ExportReference",
            ("ReferenceId", "int", true),
            ("ReferenceNo", "nvarchar", false));

        Assert.Empty(RelationshipDiscovery.BuildCandidates([reference], KeysOf(reference)));
    }

    [Fact]
    public void Tip_uyusmazligi_adayi_eler()
    {
        var orders = Table("Orders", ("Id", "int", true), ("MusteriKodu", "nvarchar", false));
        // Anahtar sayisal, kolon metin: eslesmemeli.
        var musteri = Table("Musteriler", ("Kod", "int", true));

        Assert.Empty(RelationshipDiscovery.BuildCandidates([orders, musteri], KeysOf(orders, musteri)));
    }

    [Fact]
    public void Benzersiz_anahtari_olmayan_tablo_hedef_olmaz()
    {
        // Hedef benzersiz degilse join satirlari cogaltir; aday uretilmemeli.
        var shipments = Table("Shipments", ("Id", "int", true), ("UlkeKodu", "nvarchar", false));
        var ulkeler = Table("Ulkeler", ("Kod", "nvarchar", false), ("Ad", "nvarchar", false));

        Assert.Empty(RelationshipDiscovery.BuildCandidates([shipments, ulkeler], KeysOf(shipments, ulkeler)));
    }

    [Fact]
    public void Referans_gorunumunde_olmayan_kolon_aday_uretmez()
    {
        var shipments = Table("Shipments",
            ("Id", "int", true), ("Tutar", "decimal", false), ("Aciklama", "nvarchar", false));
        var companies = Table("Companies", ("Id", "int", true));

        Assert.Empty(RelationshipDiscovery.BuildCandidates([shipments, companies], KeysOf(shipments, companies)));
    }
}

using Grafirio.DataAnalysis.Api.Features.Profile;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Iliski cikariminin ad mantigi.
///
/// Burasi sistemin "hangi kolon hangi tabloya isaret ediyor" tahminini yaptigi
/// yer. Yanlis calistiginda hicbir sey patlamaz — sorgu calisir, grafik cizilir,
/// yalnizca cevap yanlis olur. Gozle fark edilmedigi icin test ediliyor.
///
/// Buradaki vakalarin cogu uydurma degil: gercek musteri semasinda
/// (TechLogistics) cikan hatalardan geliyor.
/// </summary>
public class RelationshipNamingTests
{
    [Theory]
    [InlineData("ReceiverCompanyId", "ReceiverCompany")]
    [InlineData("MusteriKodu", "Musteri")]
    [InlineData("SiparisNo", "Siparis")]
    [InlineData("customer_id", "customer")]
    [InlineData("UrunRefId", "Urun")]
    [InlineData("FaturaNumarasi", "Fatura")]
    [InlineData("CountryCode", "Country")]
    public void StripReferenceSuffix_ekleri_atar(string column, string expected) =>
        Assert.Equal(expected, RelationshipNaming.StripReferenceSuffix(column));

    [Theory]
    [InlineData("Aciklama")]   // ek yok
    [InlineData("Tutar")]      // ek yok
    [InlineData("Id")]         // yalnizca ekten ibaret, govde kalmiyor
    [InlineData("Kod")]
    public void StripReferenceSuffix_referans_olmayani_elemez(string column) =>
        Assert.Null(RelationshipNaming.StripReferenceSuffix(column));

    [Fact]
    public void TailSegments_rol_onekini_soyar()
    {
        // "gonderici firma" ile "alici firma" ayni tabloya gider ama farkli
        // anlamlardir; onek once korunup sonra atiliyor.
        Assert.Equal(
            ["ReceiverCompany", "Company"],
            RelationshipNaming.TailSegments("ReceiverCompany"));
    }

    [Fact]
    public void NameSegments_sistem_onekli_tablo_adini_cozer()
    {
        // Gercek semadan: yalnizca tam ad karsilastirildiginda ReferenceId
        // kolonu bu tabloyu HIC bulamiyordu.
        // Ayiricilar dusuyor; karsilastirma Normalize uzerinden yapildigi icin
        // onemi yok. Onemli olan "Reference" parcasinin uretilmesi.
        Assert.Equal(
            ["LINTExportReference", "INTExportReference", "ExportReference", "Reference"],
            RelationshipNaming.NameSegments("L_INT_ExportReference"));
    }

    [Theory]
    [InlineData("Companies", "Company")]
    [InlineData("COMPANY_", "Companies")]
    [InlineData("Musteriler", "Musteri")]
    [InlineData("Siparisler", "Siparis")]
    [InlineData("Urunler", "Urun")]
    [InlineData("Shipments", "Shipment")]
    [InlineData("Countries", "Country")]
    [InlineData("Adresler", "Adres")]
    [InlineData("Dersler", "Ders")]
    public void NamesMatch_tekil_cogul_ayni_seydir(string left, string right) =>
        Assert.True(RelationshipNaming.NamesMatch(left, right), $"{left} ~ {right} eşleşmeliydi");

    [Theory]
    [InlineData("Firma", "Fatura")]
    [InlineData("Adres", "Adet")]
    [InlineData("Musteri", "Siparis")]
    [InlineData("Urun", "Ulke")]
    public void NamesMatch_benzemezleri_eslestirmez(string left, string right) =>
        Assert.False(RelationshipNaming.NamesMatch(left, right), $"{left} ~ {right} eşleşmemeliydi");

    [Theory]
    [InlineData("Adres")]
    [InlineData("Ders")]
    [InlineData("Siparis")]
    public void Variants_kendi_yazilisini_korur(string name)
    {
        // Cogul ekini yikici bicimde kirpmak Turkce'de bozuyordu: "Siparis"
        // "sipari"ye inince SiparisNo kolonu Siparisler tablosunu bulamiyordu.
        Assert.Contains(RelationshipNaming.Normalize(name), RelationshipNaming.Variants(name));
    }

    [Theory]
    [InlineData("int", "bigint", true)]
    [InlineData("varchar", "nvarchar", true)]
    [InlineData("uniqueidentifier", "uniqueidentifier", true)]
    [InlineData("int", "nvarchar", false)]
    [InlineData("datetime", "int", false)]
    public void TypesCompatible(string left, string right, bool expected) =>
        Assert.Equal(expected, RelationshipNaming.TypesCompatible(left, right));

    [Fact]
    public void LabelHints_unvani_aciklamadan_once_dener()
    {
        // Sirali olmasi sart: firma icin "Unvan" varken "Aciklama" secilmemeli.
        var hints = RelationshipNaming.LabelHints.ToList();
        Assert.True(hints.IndexOf("unvan") < hints.IndexOf("aciklama"));
    }
}

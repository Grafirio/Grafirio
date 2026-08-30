using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Agent;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Kullanicinin sisteme ogrettiklerinin kimligi ve sozluge islenmesi.
///
/// Buranin yanlis calismasinin bedeli agir: bir bilgi yanlis anahtara
/// duserse sistem ayni seyi tekrar tekrar sorar, ya da daha kotusu,
/// reddedilmis bir eslesmeyi yeniden kurar. Sozluge yanlis islenirse de
/// kullanici ogrettigini sanip unutulmus olur.
/// </summary>
public class LearnedFactTests
{
    /* ── Kimlik ───────────────────────────────────────────────────────── */

    [Fact]
    public void Ayni_bilgi_iki_kez_ogretilirse_ayni_anahtara_duser()
    {
        // Yoksa "bir daha sorma" sozu tutulamaz: her yazim yeni bir kayit
        // acar ve liste birkac turda okunmaz hale gelir.
        var first = LearnedFact.ForRelationship(
            "dbo.Siparisler", "MusteriId", "dbo.Musteriler", "Id", accepted: true);
        var second = LearnedFact.ForRelationship(
            "DBO.SIPARISLER", "musteriid", "[dbo].[Musteriler]", "ID", accepted: false);

        Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public void Iliskide_yon_anahtari_degistirir()
    {
        // a -> b ile b -> a ayni sey degil: ilki "a'nin her satiri b'den
        // birini gosterir" demek, tersi satirlari cogaltabilecek baska bir
        // iddia. Ayni anahtara duserlerse biri digerini sessizce ezer.
        var forward = LearnedFact.RelationshipKey("dbo.A", "BId", "dbo.B", "Id");
        var backward = LearnedFact.RelationshipKey("dbo.B", "Id", "dbo.A", "BId");

        Assert.NotEqual(forward, backward);
    }

    [Fact]
    public void Ayni_kolon_yeniden_tanimlanirsa_ustune_yazilir()
    {
        // Tanim anahtarin PARCASI DEGIL. Olsaydi, bir kolonun tanimini
        // duzelten kullanici eskisini de listede birakmis olurdu.
        var first = LearnedFact.ForMeaning("dbo.Siparisler", "Tutar", "KDV hariç tutar");
        var second = LearnedFact.ForMeaning("dbo.Siparisler", "Tutar", "KDV dahil tutar");

        Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public void Farkli_es_anlamlilar_ayri_kayitlardir()
    {
        // Tanimin tersi: bir kolonun birden fazla es anlamlisi olabilir ve
        // ikincisi birincisini silmemeli.
        var gelir = LearnedFact.ForSynonym("dbo.Kayit", "EarningAmount", "gelir");
        var ciro = LearnedFact.ForSynonym("dbo.Kayit", "EarningAmount", "ciro");

        Assert.NotEqual(gelir.Key, ciro.Key);
    }

    [Theory]
    [InlineData(LearnedFact.Relationship)]
    [InlineData(LearnedFact.Synonym)]
    [InlineData(LearnedFact.Meaning)]
    [InlineData(LearnedFact.CodeMeaning)]
    [InlineData(LearnedFact.Label)]
    public void Her_tur_okunabilir_bir_cumle_uretir(string kind)
    {
        // "Ogrendiklerim" ekraninda gorunen sey bu. Teknik kimlik gosterip
        // "silmek ister misiniz" diye sormak, cevaplanamayacak bir soru.
        var fact = kind switch
        {
            LearnedFact.Relationship => LearnedFact.ForRelationship(
                "dbo.A", "BId", "dbo.B", "Id", accepted: true),
            LearnedFact.Synonym => LearnedFact.ForSynonym("dbo.A", "Tutar", "ciro"),
            LearnedFact.Meaning => LearnedFact.ForMeaning("dbo.A", null, "ihracat kayıtları"),
            LearnedFact.CodeMeaning => LearnedFact.ForCodeMeaning("dbo.A", "Tip", "ROD", "karayolu"),
            _ => LearnedFact.ForLabel("dbo.A", "Unvan"),
        };

        var description = fact.Describe();
        Assert.NotEmpty(description);
        Assert.DoesNotContain(fact.Key, description);
    }

    /* ── Sozluge islenmesi ────────────────────────────────────────────── */

    private static JsonObject Dictionary() => new()
    {
        ["tables"] = new JsonArray(
            new JsonObject { ["name"] = "dbo.Kayit", ["purpose"] = "modelin tahmini" }),
        ["columns"] = new JsonArray(
            new JsonObject { ["table"] = "dbo.Kayit", ["column"] = "EarningAmount", ["role"] = "measure" }),
        ["codeValues"] = new JsonArray(
            new JsonObject
            {
                ["table"] = "dbo.Kayit",
                ["column"] = "ReferenceType",
                ["values"] = new JsonArray("ROD", "SEA")
            }),
        ["relationships"] = new JsonArray(
            new JsonObject { ["toTable"] = "dbo.Musteriler", ["labelColumn"] = "Kod" }),
    };

    [Fact]
    public void Es_anlamli_kolonun_listesine_ekleniyor()
    {
        var root = Dictionary();

        ConnectionAnalysisConsumer.ApplyLearnedFacts(root,
            [LearnedFact.ForSynonym("dbo.Kayit", "EarningAmount", "gelir")]);

        var column = (root["columns"] as JsonArray)!.OfType<JsonObject>().Single();
        var synonyms = (column["synonyms"] as JsonArray)!.Select(n => n!.GetValue<string>());

        Assert.Contains("gelir", synonyms);
        Assert.Equal("user", column["source"]!.GetValue<string>());
    }

    [Fact]
    public void Ayni_es_anlamli_iki_kez_yazilmiyor()
    {
        var root = Dictionary();
        var fact = LearnedFact.ForSynonym("dbo.Kayit", "EarningAmount", "gelir");

        ConnectionAnalysisConsumer.ApplyLearnedFacts(root, [fact, fact]);

        var column = (root["columns"] as JsonArray)!.OfType<JsonObject>().Single();
        Assert.Single((column["synonyms"] as JsonArray)!);
    }

    [Fact]
    public void Kullanicinin_tanimi_modelin_tahminini_eziyor()
    {
        var root = Dictionary();

        ConnectionAnalysisConsumer.ApplyLearnedFacts(root,
            [LearnedFact.ForMeaning("dbo.Kayit", null, "ihracat kayıtları")]);

        var table = (root["tables"] as JsonArray)!.OfType<JsonObject>().Single();
        Assert.Equal("ihracat kayıtları", table["purpose"]!.GetValue<string>());
        Assert.Equal("high", table["confidence"]!.GetValue<string>());
    }

    [Fact]
    public void Kod_anlami_olculmus_deger_listesinin_yanina_yaziliyor()
    {
        // Kullanici "kara" diyor, kolonda 'ROD' yaziyor. Ikisini birlestiren
        // tek sey bu kayit; olmadan model kodu tahmin etmek zorunda kalir.
        var root = Dictionary();

        ConnectionAnalysisConsumer.ApplyLearnedFacts(root,
            [LearnedFact.ForCodeMeaning("dbo.Kayit", "ReferenceType", "ROD", "karayolu")]);

        var entry = (root["codeValues"] as JsonArray)!.OfType<JsonObject>().Single();
        Assert.Equal("karayolu", (entry["meanings"] as JsonObject)!["ROD"]!.GetValue<string>());
    }

    [Fact]
    public void Olculmemis_kolona_kod_anlami_yazilmaya_calisilmiyor()
    {
        // Deger listesi yoksa yazacak yer de yok. Sessizce gecmeli, patlamamali.
        var root = Dictionary();

        ConnectionAnalysisConsumer.ApplyLearnedFacts(root,
            [LearnedFact.ForCodeMeaning("dbo.Kayit", "BoyleBirKolonYok", "X", "bir şey")]);

        Assert.Single((root["codeValues"] as JsonArray)!);
    }

    [Fact]
    public void Etiket_kolonu_hedef_tablonun_kenarlarinda_degistiriliyor()
    {
        // Kullanici "müşteri deyince Unvan'ı göster" diyor; J4 kurali
        // labelColumn'u okuyor, yani degistirilecek yer orasi.
        var root = Dictionary();

        ConnectionAnalysisConsumer.ApplyLearnedFacts(root,
            [LearnedFact.ForLabel("dbo.Musteriler", "Unvan")]);

        var edge = (root["relationships"] as JsonArray)!.OfType<JsonObject>().Single();
        Assert.Equal("Unvan", edge["labelColumn"]!.GetValue<string>());
    }

    [Fact]
    public void Sozlukte_olmayan_kolon_icin_kayit_aciliyor()
    {
        // Model bir kolonu hic yazmamis olabilir. Kullanicinin ogrettigi
        // bilginin kaybolmasindansa satir acilmali.
        var root = Dictionary();

        ConnectionAnalysisConsumer.ApplyLearnedFacts(root,
            [LearnedFact.ForSynonym("dbo.Kayit", "YeniKolon", "masraf")]);

        Assert.Equal(2, (root["columns"] as JsonArray)!.Count);
    }
}

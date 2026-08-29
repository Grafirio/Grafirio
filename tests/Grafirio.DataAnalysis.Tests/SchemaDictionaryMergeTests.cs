using System.Text.Json;
using Grafirio.DataAnalysis.Api.Services;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Parcali uretilen sozluklerin birlesmesi.
///
/// Neden test ediliyor: yanlis birlesen bir sozluk hata vermez. Dusen tablolar
/// sessizce yok olur, ceviri onlari bir daha secemez ve kullaniciya gorunen
/// sey "o soruyu cozemedim" olur. Sebebin burasi oldugunu anlamak icin
/// bakilacak son yer.
/// </summary>
public class SchemaDictionaryMergeTests
{
    /// <summary>
    /// Bir parcanin dondurdugu sozluk. Varsayilan olarak bir tablo iceriyor:
    /// gercek bir parca her zaman en az bir tablo dondurur ve tamamen bos bir
    /// birlesim ayri bir vaka olarak sinaniyor.
    /// </summary>
    private static string Part(string tables = """[{"name":"dbo.Varsayilan"}]""",
                               string columns = "[]",
                               string questions = "[]", string sector = "lojistik",
                               string confidence = "high") =>
        $$"""
        {"sector":"{{sector}}","sectorConfidence":"{{confidence}}",
         "tables":{{tables}},"columns":{{columns}},"questions":{{questions}}}
        """;

    private static JsonElement Merge(params string[] parts) =>
        JsonSerializer.Deserialize<JsonElement>(SchemaDictionaryMerge.Combine(parts));

    private static List<string> Names(JsonElement root, string array, string field) =>
        root.GetProperty(array).EnumerateArray()
            .Select(e => e.GetProperty(field).GetString()!)
            .ToList();

    [Fact]
    public void Tek_parca_oldugu_gibi_donuyor()
    {
        // Bolunmemis semada uretilen sozluk bit bit ayni kalmali; bu yol
        // bugune kadarki davranisin ta kendisi.
        var only = Part(tables: """[{"name":"dbo.A"}]""");

        Assert.Equal(only, SchemaDictionaryMerge.Combine([only]));
    }

    [Fact]
    public void Parcalarin_tablolari_birlesiyor()
    {
        var merged = Merge(
            Part(tables: """[{"name":"dbo.A"},{"name":"dbo.B"}]"""),
            Part(tables: """[{"name":"dbo.C"}]"""));

        Assert.Equal(["dbo.A", "dbo.B", "dbo.C"], Names(merged, "tables", "name"));
    }

    [Fact]
    public void Ayni_tablo_iki_kez_yazilmiyor()
    {
        // Tablo adlari her parcaya baglam olarak veriliyor; model uyariya
        // ragmen kendi disindaki bir tabloyu da yazabilir.
        var merged = Merge(
            Part(tables: """[{"name":"dbo.A","purpose":"ilk"}]"""),
            Part(tables: """[{"name":"dbo.A","purpose":"ikinci"},{"name":"dbo.B"}]"""));

        Assert.Equal(["dbo.A", "dbo.B"], Names(merged, "tables", "name"));
        Assert.Equal("ilk", merged.GetProperty("tables")[0].GetProperty("purpose").GetString());
    }

    [Fact]
    public void Ayni_kolon_adi_farkli_tablolarda_korunuyor()
    {
        // Tekillestirme tablo + kolon ciftine gore. Yalnizca kolon adina
        // baksaydik her tablodaki "Id" birbirini silerdi.
        var merged = Merge(
            Part(columns: """[{"table":"dbo.A","column":"Id"}]"""),
            Part(columns: """[{"table":"dbo.B","column":"Id"},{"table":"dbo.A","column":"Id"}]"""));

        Assert.Equal(2, merged.GetProperty("columns").GetArrayLength());
        Assert.Equal(["dbo.A", "dbo.B"], Names(merged, "columns", "table"));
    }

    [Fact]
    public void Sorular_bastan_numaralaniyor()
    {
        // Her parca kendi icinde q1'den basliyor; birlestirince ayni kimlikten
        // birden fazla olurdu ve kullanicinin bir soruya verdigi cevap baska
        // bir soruya yazilirdi.
        var merged = Merge(
            Part(questions: """[{"id":"q1","table":"dbo.A","column":"X","question":"?"}]"""),
            Part(questions: """[{"id":"q1","table":"dbo.B","column":"Y","question":"?"}]"""));

        Assert.Equal(["q1", "q2"], Names(merged, "questions", "id"));
    }

    [Fact]
    public void Tablo_sorulari_kolon_sorularindan_once_geliyor()
    {
        // Yanlis tablo secmek, yanlis kolon secmekten daha buyuk hata; tavana
        // takilirsa elenmesi gereken kolon sorulari.
        var merged = Merge(
            Part(questions: """[{"id":"q1","table":"dbo.A","column":"X","question":"kolon"}]"""),
            Part(questions: """[{"id":"q1","table":"dbo.B","column":null,"question":"tablo"}]"""));

        Assert.Equal(["tablo", "kolon"], Names(merged, "questions", "question"));
    }

    [Fact]
    public void Soru_sayisi_toplamda_tavana_takiliyor()
    {
        // Yedi parca ucer soru sorsa yirmi bir soruluk bir form cikardi.
        var part = Part(questions: "[" + string.Join(",",
            Enumerable.Range(1, 3).Select(i =>
                $$"""{"id":"q{{i}}","table":"dbo.A","column":"C{{i}}","question":"?"}""")) + "]");

        var merged = Merge(part, part, part, part);

        Assert.Equal(SchemaDictionaryMerge.MaxQuestions,
                     merged.GetProperty("questions").GetArrayLength());
    }

    [Fact]
    public void Sektor_agirlikli_oyla_belirleniyor()
    {
        // Ilk parcanin dedigini almak, alfabetik olarak basta duran birkac
        // tabloyu butun semanin sektoru saymak olurdu.
        var merged = Merge(
            Part(sector: "sağlık", confidence: "low"),
            Part(sector: "finans", confidence: "medium"),
            Part(sector: "finans", confidence: "high"));

        Assert.Equal("finans", merged.GetProperty("sector").GetString());
        Assert.Equal("high", merged.GetProperty("sectorConfidence").GetString());
    }

    [Fact]
    public void Diger_sektor_oy_saymiyor()
    {
        var merged = Merge(
            Part(sector: "diğer", confidence: "high"),
            Part(sector: "diğer", confidence: "high"),
            Part(sector: "üretim", confidence: "low"));

        Assert.Equal("üretim", merged.GetProperty("sector").GetString());
    }

    [Fact]
    public void Bozuk_parca_digerlerini_dusurmuyor()
    {
        var merged = Merge(
            "bu json değil",
            Part(tables: """[{"name":"dbo.A"}]"""));

        Assert.Equal(["dbo.A"], Names(merged, "tables", "name"));
    }

    [Fact]
    public void Hicbir_parca_okunamazsa_hata_veriyor()
    {
        // Sessizce bos sozluk dondurmek, analizi "hazır" isaretleyip her
        // soruyu cozulemez hale getirmek olurdu.
        var error = Assert.Throws<InvalidOperationException>(
            () => SchemaDictionaryMerge.Combine(["bozuk", "{}"]));

        Assert.Contains("okunabilir bir sonuç üretmedi", error.Message);
    }

    [Fact]
    public void Parca_yoksa_hata_veriyor() =>
        Assert.Throws<InvalidOperationException>(() => SchemaDictionaryMerge.Combine([]));

    [Fact]
    public void Sema_disi_alan_tipi_birlesmeyi_dusurmuyor()
    {
        // Model her zaman semaya uymuyor: "column" alani nesne gelirse burasi
        // patlamak yerine o kaydi atlamali.
        var merged = Merge(
            Part(columns: """[{"table":"dbo.A","column":{"tuhaf":1}}]"""),
            Part(columns: """[{"table":"dbo.A","column":"Ad"}]"""));

        Assert.Equal(["Ad"], Names(merged, "columns", "column"));
    }
}

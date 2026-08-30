using Grafirio.DataAnalysis.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Sorunun analiz parametrelerine cevrilmesi — ozellikle BOZUK bir cevabin
/// ne yapildigi.
///
/// Buranin sessiz kalmasi sahada su hatayi uretti: model cevabi yazmaya
/// baslayip token butcesi bitince ortasinda kesiliyor, yarim metin gecerli
/// bir cevap gibi asagi geciyor, ayristirilamayinca kullaniciya "model
/// gecerli bir yanit uretmedi, sorunuzu daha acik yazin" deniyordu.
/// Sorusunda bir sey yoktu ve ne kadar acik yazarsa yazsin ayni sey
/// olacakti. Ustelik modelin ne yazdigi hicbir yere kaydedilmedigi icin
/// sebep aranamiyordu.
/// </summary>
public class QuestionTranslationTests
{
    private sealed class StubLlm(string response) : ILlmClient
    {
        public bool IsConfigured => true;

        public Task<string> GenerateAsync(
            string prompt, double temperature = 0.2, int maxTokens = 2048,
            CancellationToken cancellationToken = default) => Task.FromResult(response);
    }

    private static LlmAnalysisService Service(string response) =>
        new(new StubLlm(response), NullLogger<LlmAnalysisService>.Instance);

    private static Task<LlmResult> Translate(string response) =>
        Service(response).TranslateQuestionAsync("en çok gelir", "{}", "özet");

    [Fact]
    public async Task Tam_cevap_okunuyor()
    {
        var result = await Translate("""
            ```json
            { "analysis_type": "aggregation", "target_table": "dbo.Siparisler" }
            ```
            """);

        Assert.True(result.Success);
        Assert.Contains("dbo.Siparisler", result.Json);
    }

    [Fact]
    public async Task Yarida_kesilen_cevap_basarili_sayilmiyor()
    {
        // Token butcesi bitince gelen sey tam olarak bu: acilmis ama
        // kapanmamis bir nesne.
        var result = await Translate(
            "```json\n{ \"analysis_type\": \"aggregation\", \"target_table\": \"dbo.L_INT_Export");

        Assert.False(result.Success);
        // Kullaniciya verilen ogut dogru olmali: sorusunu yeniden yazmasi
        // ise yaramaz, tekrar denemesi yarar.
        Assert.Contains("tekrar", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ic_ice_nesnenin_ortasinda_kesilen_cevap_da_basarisiz()
    {
        // Sahadaki vaka buydu: metin hem `{` hem `}` iceriyor (icteki nesne
        // kapanmis) ama disaridaki kapanmamis. ExtractJson ilk `{` ile son
        // `}` arasini aliyor ve ortaya BOZUK ama bos olmayan bir JSON
        // cikiyor. Bos nesne denetimi bunu yakalamaz; ayristirma denemesi
        // yakalar.
        var result = await Translate("""
            ```json
            { "analysis_type": "aggregation",
              "filters": { "ReferenceType": "ROD" },
              "target_table": "dbo.L_INT_Export
            """);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Ham_cevap_disari_veriliyor()
    {
        // Sunucu modelin ne yazdigini goruyor; gormedigini soylemesi icin bir
        // sebep yok. Bu alan olmadan ayni hata bir daha arastirilamaz.
        const string garbage = "Tabii, yardımcı olayım ama JSON yazmayı unuttum.";
        var result = await Translate(garbage);

        Assert.False(result.Success);
        Assert.Equal(garbage, result.RawResponse);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hiç JSON yok, düz metin.")]
    [InlineData("{}")]
    [InlineData("```json\n{}\n```")]
    public async Task Okunamayan_her_bicim_basarisiz(string response)
    {
        // Bos nesne de basarisiz sayiliyor: "model tabloyu secemedi" ile
        // "biz okuyamadik" ayni sey degil ve kullaniciya verilecek cevap da
        // ayni degil. Onceden ikisi de ayni mesaja dusuyordu.
        Assert.False((await Translate(response)).Success);
    }

    [Fact]
    public async Task Yapilandirilmamis_istemci_ayri_bir_hata()
    {
        // Bu ayrim ucta 503'e cevriliyor; "sunucu hatasi" degil "kurulum
        // eksik" demek, kullaniciyi dogru yere gonderiyor.
        var result = await new LlmAnalysisService(
            new UnconfiguredLlm(), NullLogger<LlmAnalysisService>.Instance)
            .TranslateQuestionAsync("soru", "{}", "özet");

        Assert.False(result.Success);
        Assert.True(result.IsConfigurationError);
    }

    private sealed class UnconfiguredLlm : ILlmClient
    {
        public bool IsConfigured => false;

        public Task<string> GenerateAsync(
            string prompt, double temperature = 0.2, int maxTokens = 2048,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("çağrılmamalıydı");
    }
}

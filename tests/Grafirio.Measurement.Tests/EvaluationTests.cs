using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Grafirio.Measurement.Evaluation;
using Grafirio.Measurement.Reporting;

namespace Grafirio.Measurement.Tests;

public class JsonMatchTests
{
    [Fact]
    public void Alt_kume_eslesmesi_fazladan_alanlari_yok_sayiyor()
    {
        var expected = JsonNode.Parse("""{ "target_table": "dbo.Orders", "aggregation": "count" }""");
        var actual = JsonNode.Parse("""{ "analysis_type": "aggregation", "target_table": "[dbo].[Orders]", "aggregation": "COUNT", "limit": 5 }""");

        var mismatches = new List<string>();
        Assert.True(JsonMatch.IsSubset(expected, actual, mismatches));
        Assert.Empty(mismatches);
    }

    [Fact]
    public void Dizi_sirasi_onemsiz_ama_eksik_eleman_hata()
    {
        var actual = JsonNode.Parse("""{ "group_by": ["Year", "Country"] }""");

        Assert.True(JsonMatch.IsSubset(JsonNode.Parse("""{ "group_by": ["country", "year"] }"""), actual, []));

        var mismatches = new List<string>();
        Assert.False(JsonMatch.IsSubset(JsonNode.Parse("""{ "group_by": ["City"] }"""), actual, mismatches));
        Assert.Contains(mismatches, m => m.Contains("group_by") && m.Contains("City"));
    }

    [Fact]
    public void Duzenli_ifade_beklentisi_nitelikli_kolon_adini_kabul_ediyor()
    {
        var expected = JsonNode.Parse("""{ "group_by": ["re:(^|\\.)country$"] }""");

        Assert.True(JsonMatch.IsSubset(expected, JsonNode.Parse("""{ "group_by": ["dbo.Orders.Country"] }"""), []));
        Assert.True(JsonMatch.IsSubset(expected, JsonNode.Parse("""{ "group_by": ["Country"] }"""), []));
        Assert.False(JsonMatch.IsSubset(expected, JsonNode.Parse("""{ "group_by": ["CountryCode"] }"""), []));
    }

    [Fact]
    public void Eksik_alan_yol_bilgisiyle_raporlaniyor()
    {
        var mismatches = new List<string>();
        Assert.False(JsonMatch.IsSubset(
            JsonNode.Parse("""{ "filters": { "Year": 2026 } }"""), JsonNode.Parse("""{ "filters": {} }"""), mismatches));
        Assert.Contains(mismatches, m => m.StartsWith("$.filters.Year"));
    }

    [Fact]
    public void Parmak_izi_anahtar_sirasi_ve_yazimdan_bagimsiz()
    {
        var a = JsonNode.Parse("""{ "b": [2, 1], "a": "[dbo].[X]" }""");
        var b = JsonNode.Parse("""{ "a": "dbo.x", "b": [1, 2] }""");
        var c = JsonNode.Parse("""{ "a": "dbo.y", "b": [1, 2] }""");

        Assert.Equal(JsonMatch.Fingerprint(a), JsonMatch.Fingerprint(b));
        Assert.NotEqual(JsonMatch.Fingerprint(a), JsonMatch.Fingerprint(c));
    }

    [Fact]
    public void Sonuc_parmak_izi_degisken_alanlari_yok_sayiyor()
    {
        var first = JsonNode.Parse("""{ "data": [{"k":"TR","v":10}], "executionContext": {"x":1}, "completedAt": "2026-01-01", "jobId": "a" }""");
        var second = JsonNode.Parse("""{ "data": [{"k":"TR","v":10}], "executionContext": {"x":2}, "completedAt": "2026-02-02", "jobId": "b" }""");

        Assert.Equal(JsonMatch.Fingerprint(first, EvalScoring.IsVolatileResultKey),
            JsonMatch.Fingerprint(second, EvalScoring.IsVolatileResultKey));
    }
}

public class EvalScoringTests
{
    private static EvalCase Case(string status = "completed", string? paramsJson = null) => new()
    {
        Name = "siparis-sayisi",
        Question = "Kac siparis var?",
        Expect = new EvalExpectation
        {
            Status = status,
            Params = paramsJson is null ? null : JsonNode.Parse(paramsJson)!.AsObject()
        }
    };

    private static EvalAttempt Attempt(bool passed, string fingerprint, double ms, int input = 1000, int output = 200,
        string status = "completed") => new()
    {
        Status = status,
        Passed = passed,
        TotalMs = ms,
        ParamsFingerprint = fingerprint,
        Usage = new TokenUsage(1, input, 0, output, 0, 500),
        CostUsd = 0.01
    };

    [Fact]
    public void Tutarlilik_en_sik_cevabin_payi()
    {
        Assert.Equal(1.0, EvalScoring.Consistency(["a", "a", "a"]));
        Assert.Equal(2 / 3d, EvalScoring.Consistency(["a", "b", "a"]), 6);
        Assert.Equal(1 / 3d, EvalScoring.Consistency(["a", "b", "c"]), 6);
    }

    [Fact]
    public void Hep_hata_veren_vaka_tutarli_sayilmiyor()
    {
        var attempts = new[] { Attempt(false, "e", 10, status: "error"), Attempt(false, "e", 10, status: "error") };
        var scored = EvalScoring.ScoreCase(Case(), attempts, 1.0);
        Assert.Equal(0, scored.Metrics.Single(m => m.Name == "consistency").Value);
    }

    [Fact]
    public void Netlestirme_beklenen_vakada_sorulmasi_basari()
    {
        var mismatches = new List<string>();
        Assert.True(EvalScoring.Evaluate(Case("clarification").Expect, "clarification", null, mismatches));
        Assert.False(EvalScoring.Evaluate(Case("clarification").Expect, "completed", JsonNode.Parse("{}"), mismatches));
        Assert.Contains(mismatches, m => m.Contains("clarification"));
    }

    [Fact]
    public void Olmamasi_gereken_alan_varsa_hata()
    {
        var expect = Case().Expect with { ParamsAbsent = ["joins"] };

        Assert.True(EvalScoring.Evaluate(expect, "completed", JsonNode.Parse("""{ "joins": [] }"""), []));
        var mismatches = new List<string>();
        Assert.False(EvalScoring.Evaluate(expect, "completed",
            JsonNode.Parse("""{ "joins": [{ "table": "dbo.Customers" }] }"""), mismatches));
        Assert.Contains(mismatches, m => m.Contains("joins"));
    }

    [Fact]
    public void Vaka_ancak_her_denemede_dogruysa_basarili()
    {
        var attempts = new[] { Attempt(true, "x", 1000), Attempt(false, "y", 3000), Attempt(true, "x", 2000) };

        var strict = EvalScoring.ScoreCase(Case(), attempts, passThreshold: 1.0);
        Assert.False(strict.Success);
        Assert.Equal(2 / 3d, strict.Metrics.Single(m => m.Name == "accuracy").Value, 6);
        Assert.Equal(2 / 3d, strict.Metrics.Single(m => m.Name == "consistency").Value, 6);
        Assert.Equal(2000, strict.Metrics.Single(m => m.Name == "latency.mean").Value);

        Assert.True(EvalScoring.ScoreCase(Case(), attempts, passThreshold: 0.6).Success);
    }

    [Fact]
    public void Kosu_ozeti_yuzdelik_token_ve_maliyeti_topluyor()
    {
        var attempts = Enumerable.Range(1, 10).Select(i => Attempt(i != 10, "x", i * 100)).ToList();
        var cases = new[] { EvalScoring.ScoreCase(Case(), attempts, 1.0) };

        var summary = EvalScoring.Summarize(cases, attempts).ToDictionary(m => m.Name);

        Assert.Equal(0.9, summary["accuracy"].Value, 6);
        Assert.Equal(0, summary["pass.rate"].Value);
        Assert.Equal(550, summary["latency.p50"].Value, 6);
        Assert.Equal(955, summary["latency.p95"].Value, 6);
        Assert.Equal(10_000, summary["tokens.input"].Value);
        Assert.Equal(0.1, summary["cost.total"].Value, 6);
        Assert.Equal("higher", summary["accuracy"].Direction);
        Assert.Equal("lower", summary["latency.p95"].Direction);
    }

    [Fact]
    public void Fiyat_onbellekli_girdiyi_ayri_fiyatlandiriyor()
    {
        var pricing = new LlmPricing(InputPerMillion: 2.0, CachedInputPerMillion: 0.5, OutputPerMillion: 8.0);
        var cost = pricing.Cost(new TokenUsage(1, InputTokens: 1_000_000, CachedInputTokens: 400_000, OutputTokens: 100_000, 0, 0));

        // 600k * 2 + 400k * 0.5 + 100k * 8 = 1.2 + 0.2 + 0.8
        Assert.Equal(2.2, cost, 6);
    }
}

/// <summary>
/// Kosucunun uctan uca davranisi, sahte bir Grafirio'ya karsi: gonderim, yoklama,
/// sonuc okuma ve netlestirme yolu. Sahte sunucu gercek uclarin cevap sekillerini
/// taklit ediyor (AgentQueryEndpoints).
/// </summary>
public class EvalRunnerTests
{
    [Fact]
    public async Task Set_kosuyor_ve_belgeyi_uretiyor()
    {
        var connectionId = Guid.NewGuid();
        var server = new FakeGrafirio();
        var api = new GrafirioApi(new HttpClient(server) { BaseAddress = new Uri("http://grafirio.test/") }, "data-analysis");

        var suite = new EvalSuite
        {
            Suite = "test-set",
            Cases =
            [
                new EvalCase
                {
                    Name = "siparis-sayisi", Group = "aggregation", Question = "Kac siparis var?",
                    Expect = new EvalExpectation { Params = JsonNode.Parse("""{"target_table":"dbo.Orders","aggregation":"count"}""")!.AsObject() }
                },
                new EvalCase
                {
                    Name = "belirsiz", Group = "guvenlik", Question = "Hangisi daha iyi?",
                    Expect = new EvalExpectation { Status = "clarification" }
                },
                new EvalCase
                {
                    Name = "yanlis-tablo", Question = "Musteri sayisi",
                    Expect = new EvalExpectation { Params = JsonNode.Parse("""{"target_table":"dbo.Customers"}""")!.AsObject() }
                }
            ]
        };

        var document = await new EvalRunner(api, new EvalOptions
        {
            Suite = suite,
            ConnectionId = connectionId,
            BaseUrl = "http://grafirio.test",
            Repeat = 2,
            PollInterval = TimeSpan.FromMilliseconds(1),
            Pricing = new LlmPricing(1, 1, 1)
        }).RunAsync(CancellationToken.None);

        Assert.Equal("eval", document.Kind);
        Assert.Equal(ScenarioRunDocument.SchemaV1, document.Schema);
        Assert.True(document.Cases.Single(c => c.Name == "siparis-sayisi").Success);
        Assert.True(document.Cases.Single(c => c.Name == "belirsiz").Success);

        var wrong = document.Cases.Single(c => c.Name == "yanlis-tablo");
        Assert.False(wrong.Success);
        Assert.Contains("target_table", wrong.Message);

        var metrics = document.Metrics.ToDictionary(m => m.Name);
        Assert.Equal(4 / 6d, metrics["accuracy"].Value, 6);
        Assert.Equal(6, metrics["questions"].Value);
        Assert.Equal(2 / 6d, metrics["clarification.rate"].Value, 6);
        Assert.True(metrics["cost.total"].Value > 0);

        // Soru tur tur soruluyor: ilk tur butun vakalar, sonra ikinci tur.
        Assert.Equal(["Kac siparis var?", "Hangisi daha iyi?", "Musteri sayisi", "Kac siparis var?"],
            server.Questions.Take(4));
        Assert.All(server.ConnectionIds, id => Assert.Equal(connectionId, id));
    }

    /// <summary>Soru metnine gore sabit cevap veren sahte Grafirio.</summary>
    private sealed class FakeGrafirio : HttpMessageHandler
    {
        private readonly Dictionary<Guid, string> _questions = [];
        private readonly Dictionary<Guid, int> _polls = [];
        public List<string> Questions { get; } = [];
        public List<Guid> ConnectionIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.StartsWith("/data-analysis/api/agent/", path);

            if (request.Method == HttpMethod.Post && path.EndsWith("/query"))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                var question = body["question"]!.GetValue<string>();
                Questions.Add(question);
                ConnectionIds.Add(Guid.Parse(body["connectionId"]!.GetValue<string>()));
                var id = Guid.NewGuid();
                _questions[id] = question;
                var usage = """{"calls":1,"inputTokens":1200,"cachedInputTokens":200,"outputTokens":300,"reasoningTokens":100,"durationMs":900}""";

                if (question.StartsWith("Hangisi"))
                    return Json(HttpStatusCode.BadRequest,
                        $$"""{"error":"Hangi alan?","needsClarification":true,"queryId":"{{id}}","usage":{{usage}},"preparationMs":950}""");

                return Json(HttpStatusCode.OK,
                    $$"""{"success":true,"queryId":"{{id}}","status":"processing","usage":{{usage}},"preparationMs":1000}""");
            }

            var queryId = Guid.Parse(path.Split('/')[^2]);
            if (path.EndsWith("/status"))
            {
                // Iki yoklamada biter: bekleme dongusu sinaniyor.
                _polls[queryId] = _polls.GetValueOrDefault(queryId) + 1;
                return Json(HttpStatusCode.OK, $$"""{"status":"{{(_polls[queryId] < 2 ? "processing" : "completed")}}"}""");
            }

            var table = _questions[queryId].StartsWith("Kac") ? "[dbo].[Orders]" : "dbo.Orders";
            return Json(HttpStatusCode.OK, $$"""
                {"status":"completed","llmParameters":{"target_table":"{{table}}","aggregation":"COUNT"},
                 "result":{"data":[{"v":42}],"completedAt":"{{DateTime.UtcNow:O}}"},"durationMs":1500}
                """);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

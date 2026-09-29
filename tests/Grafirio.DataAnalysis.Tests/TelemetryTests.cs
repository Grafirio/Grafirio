using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Reflection;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Olcum katmani: rakamlar kanit olarak kullanilacaksa once kendileri dogru
/// olmali. Burada sinanan seyler, yanlis olduklarinda sessizce yanlis rapor
/// ureten yerler: token toplami, kesilen cevabin maliyeti, rota etiketi.
/// </summary>
public class TelemetryTests
{
    // --- LlmUsage kapsami ----------------------------------------------------

    [Fact]
    public void Ic_kapsama_yazilan_harcama_distakine_de_yaziliyor()
    {
        using var outer = LlmUsage.Begin("question");
        using (var inner = LlmUsage.Begin("translate_question"))
        {
            LlmUsage.Record(new LlmCallUsage(100, 40, 30, 10, TimeSpan.FromMilliseconds(250)));
            Assert.Equal("translate_question", LlmUsage.Current!.Operation);
            Assert.Equal(100, inner.Usage.InputTokens);
        }

        Assert.Same(outer.Usage, LlmUsage.Current);
        Assert.Equal(1, outer.Usage.Calls);
        Assert.Equal(100, outer.Usage.InputTokens);
        Assert.Equal(40, outer.Usage.CachedInputTokens);
        Assert.Equal(30, outer.Usage.OutputTokens);
        Assert.Equal(10, outer.Usage.ReasoningTokens);
        Assert.Equal(250, outer.Usage.DurationMs);
    }

    [Fact]
    public async Task Paralel_cagrilar_ayni_kapsama_eksiksiz_yaziliyor()
    {
        // Sozluk uretimi dorderli dalgalar halinde paralel kosuyor.
        using var scope = LlmUsage.Begin("schema_dictionary");

        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() =>
            LlmUsage.Record(new LlmCallUsage(10, 0, 5, 0, TimeSpan.FromMilliseconds(1))))));

        Assert.Equal(200, scope.Usage.Calls);
        Assert.Equal(2000, scope.Usage.InputTokens);
        Assert.Equal(1000, scope.Usage.OutputTokens);
    }

    [Fact]
    public void Kapsam_yokken_kayit_sessizce_atlaniyor()
    {
        Assert.Null(LlmUsage.Current);
        LlmUsage.Record(new LlmCallUsage(1, 0, 1, 0, TimeSpan.Zero));
    }

    // --- Azure usage ayristirma ----------------------------------------------

    [Fact]
    public void Azure_usage_alani_ayrintilariyla_okunuyor()
    {
        var usage = LlmClient.ExtractUsage(AzureBody("{}", "stop", 1200, 300, cached: 1000, reasoning: 200),
            TimeSpan.FromSeconds(2))!.Value;

        Assert.Equal(1200, usage.InputTokens);
        Assert.Equal(1000, usage.CachedInputTokens);
        Assert.Equal(300, usage.OutputTokens);
        Assert.Equal(200, usage.ReasoningTokens);
    }

    [Theory]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"usage":"bozuk"}""")]
    [InlineData("json degil")]
    public void Usage_yoksa_ya_da_bozuksa_null(string body) =>
        Assert.Null(LlmClient.ExtractUsage(body, TimeSpan.Zero));

    [Fact]
    public void Ayrinti_alanlari_olmayan_eski_surum_sifir_sayiliyor()
    {
        var usage = LlmClient.ExtractUsage(
            """{"usage":{"prompt_tokens":50,"completion_tokens":7}}""", TimeSpan.Zero)!.Value;

        Assert.Equal(50, usage.InputTokens);
        Assert.Equal(0, usage.CachedInputTokens);
        Assert.Equal(0, usage.ReasoningTokens);
    }

    [Fact]
    public async Task Butceye_sigmayip_atilan_cevabin_tokenlari_da_sayiliyor()
    {
        // Ilk cevap kesiliyor (finish_reason=length), istemci butceyi buyutup
        // tekrar soruyor. Kesilen cevabin da parasi odendi: toplamda IKI cagri
        // gorunmeli, yoksa maliyet oldugundan dusuk raporlanir.
        var handler = new SequenceHandler(
            AzureBody("", "length", 1000, 4000),
            AzureBody("{\\\"ok\\\":true}", "stop", 1000, 500));
        var client = CreateClient(handler);

        using var scope = LlmUsage.Begin("translate_question");
        var content = await client.GenerateAsync("soru");

        Assert.Equal("{\"ok\":true}", content);
        Assert.Equal(2, handler.Requests);
        Assert.Equal(2, scope.Usage.Calls);
        Assert.Equal(2000, scope.Usage.InputTokens);
        Assert.Equal(4500, scope.Usage.OutputTokens);
    }

    [Fact]
    public async Task Llm_olcumleri_operasyon_etiketiyle_yayinlaniyor()
    {
        using var listener = new Recorder("grafirio.llm.tokens", "grafirio.llm.request.duration");
        var client = CreateClient(new SequenceHandler(AzureBody("{}", "stop", 900, 100, cached: 400, reasoning: 60)));

        using (LlmUsage.Begin("schema_dictionary"))
            await client.GenerateAsync("profil");

        var tokens = listener.For("grafirio.llm.tokens")
            .Where(m => (string?)m.Tags["llm.operation"] == "schema_dictionary")
            .ToDictionary(m => (string)m.Tags["llm.token.type"]!, m => m.Value);

        // input onbellekli kismi icermiyor; output reasoning'i icermiyor —
        // turlerin toplami faturadaki toplamla ayni olmali.
        Assert.Equal(500, tokens["input"]);
        Assert.Equal(400, tokens["cached_input"]);
        Assert.Equal(40, tokens["output"]);
        Assert.Equal(60, tokens["reasoning"]);

        var duration = Assert.Single(listener.For("grafirio.llm.request.duration")
            .Where(m => (string?)m.Tags["llm.operation"] == "schema_dictionary"));
        Assert.Equal("ok", duration.Tags["llm.outcome"]);
    }

    // --- Veri kaynagi ----------------------------------------------------------

    [Fact]
    public async Task Sorgu_suresi_ve_satir_sayisi_rota_etiketiyle_kaydediliyor()
    {
        using var listener = new Recorder("grafirio.datasource.query.rows", "grafirio.datasource.query.duration");
        var fake = new FakeDataSourceSession().Respond("Orders",
            FakeDataSourceSession.Row(("Id", 1)), FakeDataSourceSession.Row(("Id", 2)));
        await using var session = new InstrumentedDataSourceSession(fake, "bridge");

        var rows = await session.QueryRowsAsync("SELECT Id FROM Orders");

        Assert.Equal(2, rows.Count);
        var measurement = Assert.Single(listener.For("grafirio.datasource.query.rows")
            .Where(m => (string?)m.Tags["datasource.route"] == "bridge"));
        Assert.Equal(2, measurement.Value);
        Assert.Equal("rows", measurement.Tags["datasource.method"]);
        Assert.Contains(listener.For("grafirio.datasource.query.duration"),
            m => (string?)m.Tags["datasource.outcome"] == "ok");
    }

    [Fact]
    public async Task Hata_veren_sorgu_hata_olarak_sayiliyor_ve_istisna_korunuyor()
    {
        using var listener = new Recorder("grafirio.datasource.query.duration");
        await using var session = new InstrumentedDataSourceSession(new FakeDataSourceSession(), "direct");

        // Sahte oturum beklenmeyen sorguda hata veriyor.
        await Assert.ThrowsAnyAsync<Exception>(() => session.QueryRowsAsync("SELECT 1 FROM Bilinmeyen"));

        Assert.Contains(listener.For("grafirio.datasource.query.duration"),
            m => (string?)m.Tags["datasource.outcome"] == "error"
                 && (string?)m.Tags["datasource.route"] == "direct");
    }

    [Fact]
    public async Task Akis_yarida_birakilinca_ok_ve_okunan_satir_kadar_sayiliyor()
    {
        using var listener = new Recorder("grafirio.datasource.query.rows");
        var fake = new FakeDataSourceSession().Respond("Orders",
            FakeDataSourceSession.Row(("Id", 1)), FakeDataSourceSession.Row(("Id", 2)),
            FakeDataSourceSession.Row(("Id", 3)));
        await using var session = new InstrumentedDataSourceSession(fake, "direct");

        await foreach (var _ in session.StreamAsync("SELECT Id FROM Orders"))
            break;

        var measurement = Assert.Single(listener.For("grafirio.datasource.query.rows")
            .Where(m => (string?)m.Tags["datasource.method"] == "stream"));
        Assert.Equal(1, measurement.Value);
    }

    // --- Geri bildirim ---------------------------------------------------------

    [Fact]
    public async Task Geri_bildirim_kayda_yaziliyor_ve_sayaca_giriyor()
    {
        using var listener = new Recorder("grafirio.feedback");
        await using var harness = new OwnedAnalysisEndpointHarness();
        var query = new Grafirio.DataAnalysis.Api.Data.Entities.QueryHistory
        {
            Id = Guid.NewGuid(), ConfigId = harness.Config.Id, Question = "kac siparis",
            Status = "completed", CreatedAt = DateTime.UtcNow
        };
        harness.Db.QueryHistories.Add(query);
        await harness.Db.SaveChangesAsync();

        var result = await Feedback(harness, query.Id, new AgentQueryEndpoints.FeedbackRequest(-1, "  yanlis tablo  "));

        Assert.IsType<NoContent>(result);
        var stored = await harness.Db.QueryHistories.AsNoTracking().SingleAsync(q => q.Id == query.Id);
        Assert.Equal((short)-1, stored.FeedbackRating);
        Assert.Equal("yanlis tablo", stored.FeedbackComment);
        Assert.NotNull(stored.FeedbackAt);
        Assert.Contains(listener.For("grafirio.feedback"), m => (string?)m.Tags["feedback.rating"] == "down");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task Gecersiz_oy_reddediliyor(int rating)
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        var result = await Feedback(harness, Guid.NewGuid(), new AgentQueryEndpoints.FeedbackRequest(rating));
        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Baska_sirketin_sorgusuna_oy_verilemiyor()
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        var foreignConfig = new Grafirio.DataAnalysis.Api.Data.Entities.AnalysisConfig
        {
            Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid(), CompanyId = Guid.NewGuid().ToString()
        };
        var foreign = new Grafirio.DataAnalysis.Api.Data.Entities.QueryHistory
        {
            Id = Guid.NewGuid(), ConfigId = foreignConfig.Id, Status = "completed", CreatedAt = DateTime.UtcNow
        };
        harness.Db.AddRange(foreignConfig, foreign);
        await harness.Db.SaveChangesAsync();

        var result = await Feedback(harness, foreign.Id, new AgentQueryEndpoints.FeedbackRequest(1));

        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Null((await harness.Db.QueryHistories.AsNoTracking().SingleAsync(q => q.Id == foreign.Id)).FeedbackRating);
    }

    // --- Yardimcilar -----------------------------------------------------------

    private static Task<IResult> Feedback(OwnedAnalysisEndpointHarness harness, Guid queryId,
        AgentQueryEndpoints.FeedbackRequest request)
    {
        var method = typeof(AgentQueryEndpoints).GetMethod("SubmitFeedback", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Task<IResult>)method.Invoke(null,
            [queryId, request, harness.Db, harness.Identity.Object, CancellationToken.None])!;
    }

    private static string AzureBody(string content, string finishReason, int prompt, int completion,
        int cached = 0, int reasoning = 0) =>
        "{\"choices\":[{\"message\":{\"content\":\"" + content + "\"},\"finish_reason\":\"" + finishReason + "\"}]," +
        "\"usage\":{\"prompt_tokens\":" + prompt + ",\"completion_tokens\":" + completion +
        ",\"prompt_tokens_details\":{\"cached_tokens\":" + cached + "}" +
        ",\"completion_tokens_details\":{\"reasoning_tokens\":" + reasoning + "}}}";

    private static LlmClient CreateClient(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.invalid",
            ["AzureOpenAI:Deployment"] = "test-deployment",
            ["AzureOpenAI:ApiKey"] = "test-key",
        }).Build();
        return new LlmClient(factory.Object, configuration, NullLogger<LlmClient>.Instance);
    }

    private sealed class SequenceHandler(params string[] bodies) : HttpMessageHandler
    {
        private int _index;
        public int Requests => _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = bodies[Math.Min(_index, bodies.Length - 1)];
            Interlocked.Increment(ref _index);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// Grafirio olcumlerini dinleyip bellekte tutar. Testler paralel kostugu
    /// icin olcumler etiketle suzuluyor; baska testin olcumu karisabilir ama
    /// testin kendi benzersiz etiketi karismaz.
    /// </summary>
    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, Dictionary<string, object?> Tags)> _measurements = [];
        private readonly Lock _lock = new();

        public Recorder(params string[] instruments)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == GrafirioTelemetry.SourceName && instruments.Contains(instrument.Name))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i.Name, v, t));
            _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i.Name, v, t));
            _listener.Start();
        }

        private void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags) copy[tag.Key] = tag.Value;
            lock (_lock) _measurements.Add((name, value, copy));
        }

        public List<(double Value, Dictionary<string, object?> Tags)> For(string name)
        {
            lock (_lock)
                return _measurements.Where(m => m.Name == name).Select(m => (m.Value, m.Tags)).ToList();
        }

        public void Dispose() => _listener.Dispose();
    }
}

using System.Net;
using System.Net.Sockets;
using Grafirio.Measurement.Cli;
using Grafirio.Measurement.Evaluation;
using Grafirio.Measurement.Load;
using Grafirio.Measurement.Reporting;
using Grafirio.Measurement.Usage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Grafirio.Measurement.Tests;

public class StatsTests
{
    [Fact]
    public void Yuzdelik_dogrusal_ara_degerleme_ile()
    {
        double[] values = [15, 20, 35, 40, 50];
        Assert.Equal(35, Stats.Percentile(values, 50));
        Assert.Equal(15, Stats.Percentile(values, 0));
        Assert.Equal(50, Stats.Percentile(values, 100));
        Assert.Equal(48, Stats.Percentile(values, 95), 6);
        Assert.True(double.IsNaN(Stats.Percentile([], 50)));
    }
}

public class LoadRunnerTests
{
    [Fact]
    public void Ozet_sinir_icindeki_en_yuksek_verimi_ve_olceklenmeyi_veriyor()
    {
        var options = new LoadOptions
        {
            Scenario = "t", BaseUrl = "http://x", Request = new LoadRequest(HttpMethod.Get, "health"),
            P95SloMs = 500, MaxErrorRate = 0.01
        };
        StageResult Stage(int c, double rps, double p95, int errors = 0) =>
            new(c, 1000, errors, rps, p95 / 2, p95, p95, p95, [], []);

        var metrics = LoadRunner.Summarize(
        [
            Stage(1, 100, 20),
            Stage(10, 800, 120),
            Stage(50, 900, 900) // p95 sinirin ustunde: sayilmiyor
        ], options).ToDictionary(m => m.Name);

        Assert.Equal(800, metrics["throughput"].Value);
        Assert.Equal(10, metrics["concurrency.max_healthy"].Value);
        Assert.Equal(120, metrics["latency.p95"].Value);
        // 10 kat kullanici, 8 kat verim.
        Assert.Equal(0.8, metrics["scaling.efficiency"].Value, 6);
    }

    [Fact]
    public async Task Gercek_sunucuya_karsi_kademeler_olculuyor_ve_sinir_asilinca_duruyor()
    {
        // Ayakta gercek bir Kestrel: ilk kademe saglikli, ikinci kademede her
        // ikinci istek 500 donuyor. Kosucu ikinci kademede durmali.
        var port = FreePort();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        var counter = 0;
        var concurrent = 0;
        app.MapGet("/health", async () =>
        {
            var inFlight = Interlocked.Increment(ref concurrent);
            try
            {
                await Task.Delay(5);
                return inFlight > 1 && Interlocked.Increment(ref counter) % 2 == 0
                    ? Results.StatusCode(500)
                    : Results.Ok();
            }
            finally
            {
                Interlocked.Decrement(ref concurrent);
            }
        });
        await app.StartAsync();

        var document = await new LoadRunner(new LoadOptions
        {
            Scenario = "health",
            BaseUrl = $"http://127.0.0.1:{port}",
            Request = new LoadRequest(HttpMethod.Get, "health"),
            Stages = [1, 8, 16],
            StageDuration = TimeSpan.FromMilliseconds(700),
            Warmup = TimeSpan.Zero,
            MaxErrorRate = 0.05,
            P95SloMs = 5000
        }).RunAsync(CancellationToken.None);

        Assert.Equal("load", document.Kind);
        Assert.Equal("load-health", document.Suite);
        Assert.Equal(2, document.Cases.Count); // 16'ya gecilmedi
        Assert.True(document.Cases[0].Success);
        Assert.False(document.Cases[1].Success);
        Assert.Contains("8 kullanicida", document.Environment["stopReason"]);

        var first = document.Cases[0].Metrics.ToDictionary(m => m.Name);
        Assert.True(first["requests"].Value > 10);
        Assert.True(first["throughput"].Value > 0);
        Assert.Equal(0, first["error.rate"].Value);
        Assert.All(document.Metrics, m => Assert.True(double.IsFinite(m.Value)));

        await app.StopAsync();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

public class UsageReportTests
{
    private static readonly DateTime Monday = new(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Config = Guid.NewGuid();

    private static QuestionRow Q(string user, string text, DateTime at, string status = "completed",
        double seconds = 10, short? rating = null) =>
        new(Config, user, text, status, at, status == "completed" ? at.AddSeconds(seconds) : at, 2000, 1000, 0, 200,
            rating);

    [Fact]
    public void Ayni_soru_pencere_icinde_tekrar_sorulursa_tekrar_sayiliyor()
    {
        var rows = new[]
        {
            Q("u1", "Kaç sipariş var?", Monday),
            Q("u1", "kaç   sipariş var", Monday.AddMinutes(3)),         // tekrar
            Q("u2", "Kaç sipariş var?", Monday.AddMinutes(4)),           // baska kullanici
            Q("u1", "Kaç sipariş var?", Monday.AddDays(3))               // pencere disi
        };

        var reasks = UsageReport.FindReasks(rows);

        Assert.Single(reasks);
        Assert.Contains(rows[1], reasks);
    }

    [Fact]
    public void Rapor_haftalik_kirilim_ve_toplu_metrik_uretiyor_kimlik_tasimiyor()
    {
        var rows = new[]
        {
            Q("u1", "a", Monday, seconds: 10, rating: 1),
            Q("u2", "b", Monday.AddDays(1), seconds: 30, rating: -1),
            Q("u1", "c", Monday.AddDays(2), status: "clarification"),
            Q("u3", "d", Monday.AddDays(8), status: "failed")
        };
        var analyses = new[] { new AnalysisRow("ready", Monday, 60_000, 50_000, 0, 8_000) };

        var document = UsageReport.Build(rows, analyses, Monday.Date, Monday.Date.AddDays(14), UsageBucket.Week,
            new LlmPricing(1, 1, 1), "postgres://db", null);

        Assert.Equal("usage", document.Kind);
        Assert.Equal(["2026-W39", "2026-W40"], document.Cases.Select(c => c.Name));

        var metrics = document.Metrics.ToDictionary(m => m.Name);
        Assert.Equal(4, metrics["questions"].Value);
        Assert.Equal(3, metrics["users.active"].Value);
        Assert.Equal(0.5, metrics["completed.rate"].Value);
        Assert.Equal(0.5, metrics["feedback.positive.rate"].Value);
        // Bekleme = hazirlik (2 sn) + satirdan bitise: 12 sn ve 32 sn.
        Assert.Equal(22_000, metrics["latency.p50"].Value, 6);
        Assert.Equal(1, metrics["analysis.success.rate"].Value);

        var json = System.Text.Json.JsonSerializer.Serialize(document, ScenarioRunDocument.JsonOptions);
        Assert.DoesNotContain("u1", json);
        Assert.DoesNotContain(Config.ToString(), json);
    }
}

public class CommandLineTests
{
    [Fact]
    public void Secenekler_bayraklar_ve_konumsal_argumanlar_okunuyor()
    {
        var cli = new CommandLine(["dosya.json", "--repeat", "5", "--allow-llm", "--duration=2m", "--stages", "1,2"]);

        Assert.Equal(["dosya.json"], cli.Positionals);
        Assert.Equal(5, cli.Int("repeat", 3));
        Assert.True(cli.Flag("allow-llm"));
        Assert.Equal(TimeSpan.FromMinutes(2), cli.Duration("duration", TimeSpan.Zero));
        Assert.Equal("1,2", cli.Get("stages"));
        Assert.Empty(cli.Unknown);
    }

    [Fact]
    public void Okunmayan_secenek_bilinmeyen_sayiliyor()
    {
        var cli = new CommandLine(["--reapet", "5"]);
        Assert.Equal(3, cli.Int("repeat", 3));
        Assert.Equal(["reapet"], cli.Unknown);
    }
}

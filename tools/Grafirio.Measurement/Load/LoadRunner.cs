using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Grafirio.Measurement.Reporting;

namespace Grafirio.Measurement.Load;

/// <summary>Yuk testinde tekrarlanan tek istek.</summary>
public sealed record LoadRequest(HttpMethod Method, string Path, string? JsonBody = null)
{
    public HttpRequestMessage Build() => new(Method, Path)
    {
        Content = JsonBody is null ? null : new StringContent(JsonBody, Encoding.UTF8, "application/json")
    };
}

public sealed record LoadOptions
{
    public required string Scenario { get; init; }
    public required string BaseUrl { get; init; }
    public required LoadRequest Request { get; init; }
    public string? Token { get; init; }

    /// <summary>Kademeler: her biri bir eszamanli kullanici sayisi.</summary>
    public IReadOnlyList<int> Stages { get; init; } = [1, 5, 10, 25, 50];

    public TimeSpan StageDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan Warmup { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// "Kabul edilebilir" sinirlari: bir kademe bunlari asarsa sistem o yukte
    /// hizmet veremiyor demektir ve sonraki kademeler kosturulmuyor.
    /// </summary>
    public double MaxErrorRate { get; init; } = 0.01;
    public double P95SloMs { get; init; } = 2000;

    public string? Label { get; init; }
}

/// <summary>
/// Kapali dongu yuk testi: N sanal kullanici, her biri cevabi alinca bir sonraki
/// istegi gonderiyor. Acik dongunun (sabit istek/sn) aksine sistem yavasladikca
/// yuk de azaliyor — bu, gercek kullanicilarin davranisina daha yakin ve
/// "kac eszamanli kullaniciyi kaldiriyor" sorusunu dogrudan cevapliyor.
/// </summary>
public sealed class LoadRunner(LoadOptions options)
{
    public async Task<ScenarioRunDocument> RunAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var handler = new SocketsHttpHandler
        {
            // Varsayilan havuz sinirsiz ama baglantilarin yeniden kullanimi
            // olculen seyi etkilemesin diye acikca belirtiliyor.
            MaxConnectionsPerServer = Math.Max(options.Stages.Max() * 2, 100),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = options.RequestTimeout
        };
        if (!string.IsNullOrEmpty(options.Token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);

        if (options.Warmup > TimeSpan.Zero)
        {
            Console.WriteLine($"[load] Isinma {options.Warmup.TotalSeconds:0} sn (olculmuyor)…");
            await RunStageAsync(http, 1, options.Warmup, ct);
        }

        var cases = new List<Case>();
        var stageResults = new List<StageResult>();
        string? stopReason = null;

        foreach (var concurrency in options.Stages)
        {
            Console.WriteLine($"[load] {concurrency} eszamanli kullanici, {options.StageDuration.TotalSeconds:0} sn…");
            var result = await RunStageAsync(http, concurrency, options.StageDuration, ct);
            stageResults.Add(result);

            var healthy = result.ErrorRate <= options.MaxErrorRate && result.P95Ms <= options.P95SloMs;
            Console.WriteLine(
                $"[load]   {result.Throughput:0.0} istek/sn · p50 {result.P50Ms:0.#} ms · p95 {result.P95Ms:0.#} ms · " +
                $"hata %{result.ErrorRate * 100:0.##}{(healthy ? "" : "  ← sinir asildi")}");

            cases.Add(ToCase(result, healthy));

            if (!healthy)
            {
                stopReason = $"{concurrency} kullanicida sinir asildi " +
                             $"(hata %{result.ErrorRate * 100:0.##}, p95 {result.P95Ms:0} ms); sonraki kademeler kosturulmadi.";
                break;
            }
        }

        var (commit, branch) = RunOutput.Git();
        return new ScenarioRunDocument
        {
            Kind = "load",
            Suite = $"load-{options.Scenario}",
            Label = options.Label,
            StartedAt = started,
            FinishedAt = DateTimeOffset.UtcNow,
            GitCommit = commit,
            GitBranch = branch,
            Target = options.BaseUrl,
            Environment = new Dictionary<string, string>
            {
                ["request"] = $"{options.Request.Method} /{options.Request.Path}",
                ["stages"] = string.Join(",", options.Stages),
                ["stageDuration"] = $"{options.StageDuration.TotalSeconds:0}s",
                ["slo"] = $"hata <= %{options.MaxErrorRate * 100:0.##}, p95 <= {options.P95SloMs:0} ms",
                ["stopReason"] = stopReason ?? "tum kademeler sinir icinde"
            },
            Metrics = Summarize(stageResults, options),
            Cases = cases
        };
    }

    /// <summary>
    /// Kosu geneli: sinir icinde kalan en yuksek verim ve o verimi saglayan
    /// kullanici sayisi. "Sistem X kullaniciya kadar p95 &lt; Y ms ile Z istek/sn
    /// kaldiriyor" cumlesinin rakamlari bunlar.
    /// </summary>
    public static List<Metric> Summarize(IReadOnlyList<StageResult> stages, LoadOptions options)
    {
        var metrics = new List<Metric>();
        if (stages.Count == 0) return metrics;

        var healthy = stages.Where(s => s.ErrorRate <= options.MaxErrorRate && s.P95Ms <= options.P95SloMs).ToList();
        var best = healthy.MaxBy(s => s.Throughput);

        if (best is not null)
        {
            metrics.Add(Metric.Higher("throughput", best.Throughput, "req/s"));
            metrics.Add(Metric.Higher("concurrency.max_healthy", healthy.Max(s => s.Concurrency), "users"));
            metrics.Add(Metric.Lower("latency.p95", best.P95Ms, "ms"));
        }
        else
        {
            metrics.Add(Metric.Higher("concurrency.max_healthy", 0, "users"));
        }

        var first = stages[0];
        metrics.Add(Metric.Lower("latency.p50", first.P50Ms, "ms"));
        metrics.Add(Metric.Lower("latency.p95.single_user", first.P95Ms, "ms"));
        metrics.Add(Metric.Lower("error.rate", stages.Sum(s => s.Errors) / (double)Math.Max(1, stages.Sum(s => s.Requests)), "ratio"));
        metrics.Add(Metric.Info("requests", stages.Sum(s => s.Requests), "count"));

        // Olceklenme verimi: kullanici sayisi k kat artinca verim kac kat artti.
        // 1.0 dogrusal olceklenme, 0'a yakin "daha fazla kullanici hicbir sey
        // kazandirmiyor" demek.
        var last = healthy.LastOrDefault();
        if (last is not null && last.Concurrency > first.Concurrency && first.Throughput > 0)
        {
            var efficiency = last.Throughput / first.Throughput / (last.Concurrency / (double)first.Concurrency);
            metrics.Add(Metric.Higher("scaling.efficiency", efficiency, "ratio"));
        }

        return metrics.Where(m => double.IsFinite(m.Value)).ToList();
    }

    private static Case ToCase(StageResult result, bool healthy) => new()
    {
        Name = $"c={result.Concurrency:000}",
        Group = "stage",
        Success = healthy,
        Message = healthy ? null : "Sinir asildi",
        Metrics = new List<Metric>
        {
            Metric.Higher("throughput", result.Throughput, "req/s"),
            Metric.Lower("latency.p50", result.P50Ms, "ms"),
            Metric.Lower("latency.p95", result.P95Ms, "ms"),
            Metric.Lower("latency.p99", result.P99Ms, "ms"),
            Metric.Lower("latency.max", result.MaxMs, "ms"),
            Metric.Lower("error.rate", result.ErrorRate, "ratio"),
            Metric.Info("requests", result.Requests, "count")
        }
            // Hic istek tamamlanamadiysa yuzdelikler NaN; dashboard bunlari reddeder.
            .Where(m => double.IsFinite(m.Value)).ToList(),
        Details = new { result.Concurrency, result.StatusCounts, result.SampleErrors }
    };

    private async Task<StageResult> RunStageAsync(HttpClient http, int concurrency, TimeSpan duration,
        CancellationToken ct)
    {
        var latencies = new ConcurrentBag<double>();
        var statusCounts = new ConcurrentDictionary<string, int>();
        var sampleErrors = new ConcurrentQueue<string>();
        var errors = 0;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(duration);
        var clock = Stopwatch.StartNew();

        var workers = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var started = Stopwatch.GetTimestamp();
                string key;
                var failed = false;

                try
                {
                    using var request = options.Request.Build();
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
                    key = ((int)response.StatusCode).ToString();
                    failed = (int)response.StatusCode >= 500 || (int)response.StatusCode is 401 or 403 or 429;
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    // Kademe bitti; yarida kalan istek sayilmiyor.
                    break;
                }
                catch (Exception exception) when (!ct.IsCancellationRequested)
                {
                    key = exception.GetType().Name;
                    failed = true;
                    if (sampleErrors.Count < 5) sampleErrors.Enqueue(exception.Message);
                }

                latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                statusCounts.AddOrUpdate(key, 1, (_, n) => n + 1);
                if (failed) Interlocked.Increment(ref errors);
            }
        }, CancellationToken.None)).ToList();

        await Task.WhenAll(workers);
        clock.Stop();

        var values = latencies.ToList();
        return new StageResult(
            concurrency,
            values.Count,
            errors,
            values.Count / clock.Elapsed.TotalSeconds,
            Stats.Percentile(values, 50),
            Stats.Percentile(values, 95),
            Stats.Percentile(values, 99),
            values.Count == 0 ? double.NaN : values.Max(),
            statusCounts.ToDictionary(p => p.Key, p => p.Value),
            sampleErrors.ToList());
    }
}

public sealed record StageResult(
    int Concurrency,
    int Requests,
    int Errors,
    double Throughput,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    Dictionary<string, int> StatusCounts,
    List<string> SampleErrors)
{
    public double ErrorRate => Requests == 0 ? 1 : Errors / (double)Requests;
}

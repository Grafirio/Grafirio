using System.Text.Json.Nodes;
using Grafirio.Measurement.Reporting;

namespace Grafirio.Measurement.Evaluation;

/// <summary>Bir sorunun bir kez sorulmasinin sonucu.</summary>
public sealed record EvalAttempt
{
    public int Index { get; init; }

    /// <summary>completed | clarification | failed | cancelled | timeout | error.</summary>
    public required string Status { get; init; }

    public bool Passed { get; init; }

    /// <summary>Kullanicinin bekledigi sure: gonderimden son duruma kadar.</summary>
    public double TotalMs { get; init; }

    /// <summary>Sunucunun olctugu hazirlik (yetki + LLM cevirisi) suresi.</summary>
    public double? PreparationMs { get; init; }

    public TokenUsage? Usage { get; init; }
    public double? CostUsd { get; init; }

    /// <summary>LLM parametrelerinin kanonik parmak izi — tutarlilik bununla olculuyor.</summary>
    public string? ParamsFingerprint { get; init; }

    /// <summary>Sonuc verisinin parmak izi (degisken alanlar haric).</summary>
    public string? ResultFingerprint { get; init; }

    public List<string> Mismatches { get; init; } = [];
    public string? Error { get; init; }
    public Guid? QueryId { get; init; }
    public JsonNode? Parameters { get; init; }
}

/// <summary>
/// Denemeleri vaka ve kosu metriklerine ceviren saf hesap. Ag ve saat yok; bu
/// yuzden testle birebir dogrulanabiliyor.
/// </summary>
public static class EvalScoring
{
    /// <summary>
    /// Bir vakanin sonucu. Vaka "basarili" sayilmak icin her denemede dogru
    /// cevap vermeli (<paramref name="passThreshold"/> = 1): kanit olarak
    /// kullanilacak bir rakamda "genelde dogru" yeterli degil. Esik
    /// <c>--pass-threshold</c> ile gevsetilebilir.
    /// </summary>
    public static Case ScoreCase(EvalCase evalCase, IReadOnlyList<EvalAttempt> attempts, double passThreshold)
    {
        var accuracy = attempts.Count == 0 ? 0 : attempts.Count(a => a.Passed) / (double)attempts.Count;
        var metrics = new List<Metric>
        {
            Metric.Higher("accuracy", accuracy, "ratio"),
            // Hata/zaman asimi "cevap" degil: her denemesi hata veren bir vaka
            // "hep ayni cevabi verdi" diye %100 tutarli gorunmemeli.
            Metric.Higher("consistency", Consistency(attempts
                .Where(a => a.Status is not ("error" or "timeout"))
                .Select(a => a.ParamsFingerprint ?? a.Status)), "ratio"),
            Metric.Info("attempts", attempts.Count, "count")
        };

        var results = attempts.Where(a => a.ResultFingerprint is not null).Select(a => a.ResultFingerprint!).ToList();
        if (results.Count > 1) metrics.Add(Metric.Higher("consistency.result", Consistency(results), "ratio"));

        var latencies = attempts.Select(a => a.TotalMs).ToList();
        metrics.AddIfFinite(Metric.Lower("latency.mean", Stats.Mean(latencies), "ms"));
        metrics.AddIfFinite(Metric.Lower("latency.max", latencies.Count == 0 ? double.NaN : latencies.Max(), "ms"));

        var preparation = attempts.Where(a => a.PreparationMs is not null).Select(a => a.PreparationMs!.Value).ToList();
        metrics.AddIfFinite(Metric.Lower("preparation.mean", Stats.Mean(preparation), "ms"));

        var usages = attempts.Where(a => a.Usage is not null).Select(a => a.Usage!).ToList();
        if (usages.Count > 0)
        {
            metrics.Add(Metric.Lower("tokens.input", usages.Average(u => u.InputTokens), "tokens"));
            metrics.Add(Metric.Lower("tokens.output", usages.Average(u => u.OutputTokens), "tokens"));
        }

        var costs = attempts.Where(a => a.CostUsd is not null).Select(a => a.CostUsd!.Value).ToList();
        metrics.AddIfFinite(Metric.Lower("cost.per_question", Stats.Mean(costs), "USD"));

        var failures = attempts.Where(a => !a.Passed).ToList();
        var message = failures.Count == 0
            ? null
            : string.Join(" | ", failures
                .Select(a => a.Error ?? (a.Mismatches.Count > 0 ? string.Join("; ", a.Mismatches) : $"durum: {a.Status}"))
                .Distinct()
                .Take(3));

        return new Case
        {
            Name = evalCase.Name,
            Group = evalCase.Group,
            Success = attempts.Count > 0 && accuracy >= passThreshold,
            Message = message,
            Metrics = metrics,
            Details = new
            {
                question = evalCase.Question,
                expect = evalCase.Expect,
                attempts = attempts.Select(a => new
                {
                    a.Index, a.Status, a.Passed, totalMs = Math.Round(a.TotalMs), a.PreparationMs, a.Usage,
                    a.CostUsd, a.ParamsFingerprint, a.ResultFingerprint, a.Mismatches, a.Error, a.QueryId,
                    parameters = a.Parameters
                })
            }
        };
    }

    /// <summary>Kosu geneli metrikler: tum denemeler uzerinden.</summary>
    public static List<Metric> Summarize(IReadOnlyList<Case> cases, IReadOnlyList<EvalAttempt> attempts)
    {
        var metrics = new List<Metric>();
        var total = attempts.Count;
        if (total == 0) return metrics;

        metrics.Add(Metric.Higher("accuracy", attempts.Count(a => a.Passed) / (double)total, "ratio"));
        metrics.Add(Metric.Higher("pass.rate", cases.Count(c => c.Success) / (double)cases.Count, "ratio"));
        metrics.Add(Metric.Higher("consistency", cases
            .Select(c => c.Metrics.First(m => m.Name == "consistency").Value).Average(), "ratio"));
        metrics.Add(Metric.Lower("error.rate",
            attempts.Count(a => a.Status is "failed" or "error" or "timeout") / (double)total, "ratio"));
        metrics.Add(Metric.Info("clarification.rate",
            attempts.Count(a => a.Status == "clarification") / (double)total, "ratio"));

        var latencies = attempts.Select(a => a.TotalMs).ToList();
        metrics.Add(Metric.Lower("latency.p50", Stats.Percentile(latencies, 50), "ms"));
        metrics.Add(Metric.Lower("latency.p95", Stats.Percentile(latencies, 95), "ms"));
        metrics.Add(Metric.Lower("latency.p99", Stats.Percentile(latencies, 99), "ms"));
        metrics.Add(Metric.Lower("latency.mean", Stats.Mean(latencies), "ms"));

        var usages = attempts.Where(a => a.Usage is not null).Select(a => a.Usage!).ToList();
        if (usages.Count > 0)
        {
            metrics.Add(Metric.Lower("tokens.input", usages.Sum(u => u.InputTokens), "tokens"));
            metrics.Add(Metric.Lower("tokens.output", usages.Sum(u => u.OutputTokens), "tokens"));
            metrics.Add(Metric.Lower("tokens.per_question",
                usages.Average(u => u.InputTokens + u.OutputTokens), "tokens"));
        }

        var costs = attempts.Where(a => a.CostUsd is not null).Select(a => a.CostUsd!.Value).ToList();
        if (costs.Count > 0)
        {
            metrics.Add(Metric.Lower("cost.total", costs.Sum(), "USD"));
            metrics.Add(Metric.Lower("cost.per_question", costs.Average(), "USD"));
        }

        metrics.Add(Metric.Info("questions", total, "count"));
        return metrics;
    }

    /// <summary>
    /// En sik gorulen cevabin payi. 3 denemede ayni cevap = 1.0; uc farkli
    /// cevap = 0.33. Tek deneme tutarlilik hakkinda bir sey soylemez ama 1.0
    /// sayilir — tekrar sayisi (<c>--repeat</c>) bu yuzden onemli.
    /// </summary>
    public static double Consistency(IEnumerable<string> answers)
    {
        var list = answers.ToList();
        if (list.Count == 0) return 0;
        return list.GroupBy(a => a).Max(g => g.Count()) / (double)list.Count;
    }

    /// <summary>
    /// Bir denemenin beklentiyi karsilayip karsilamadigi. Uyusmazliklar
    /// <paramref name="mismatches"/>'e yaziliyor.
    /// </summary>
    public static bool Evaluate(EvalExpectation expect, string status, JsonNode? parameters, List<string> mismatches)
    {
        var expectedStatus = expect.Status ?? "completed";
        if (status != expectedStatus)
        {
            mismatches.Add($"durum: '{expectedStatus}' bekleniyordu, '{status}' geldi");
            return false;
        }

        // Netlestirme beklenen vakada parametre yok; sorulmus olmasi yeterli.
        if (expectedStatus != "completed") return true;

        var ok = JsonMatch.IsSubset(expect.Params, parameters, mismatches);

        foreach (var key in expect.ParamsAbsent ?? [])
        {
            if (parameters is JsonObject obj && obj.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)
                                                             && p.Value is not null
                                                             && p.Value.ToJsonString() is not ("[]" or "{}" or "null" or "\"\"")))
            {
                mismatches.Add($"$.{key}: bulunmamasi gerekiyordu");
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>
    /// Sonuc parmak izinde yok sayilan alanlar: her calistirmada degisen
    /// kimlikler, zaman damgalari ve sureler.
    /// </summary>
    public static bool IsVolatileResultKey(string key) =>
        key.Equals("executionContext", StringComparison.OrdinalIgnoreCase)
        || key.Equals("message", StringComparison.OrdinalIgnoreCase)
        || key.Equals("timestamp", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("At", StringComparison.Ordinal)
        || key.EndsWith("Ms", StringComparison.Ordinal)
        || key.EndsWith("Id", StringComparison.Ordinal)
        || key.EndsWith("_at", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("_ms", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
        || key.Contains("duration", StringComparison.OrdinalIgnoreCase)
        || key.Contains("elapsed", StringComparison.OrdinalIgnoreCase);
}

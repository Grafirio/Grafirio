using System.Diagnostics;
using Grafirio.Measurement.Reporting;

namespace Grafirio.Measurement.Evaluation;

public sealed record EvalOptions
{
    public required EvalSuite Suite { get; init; }
    public required Guid ConnectionId { get; init; }
    public required string BaseUrl { get; init; }
    public int Repeat { get; init; } = 3;
    public double PassThreshold { get; init; } = 1.0;
    public TimeSpan QuestionTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public LlmPricing? Pricing { get; init; }
    public string? Label { get; init; }
    public string? CaseFilter { get; init; }
}

/// <summary>
/// Seti sirayla kosturur. Sirali, cunku olculen sey tek bir kullanicinin
/// bekledigi sure; paralel sorular birbirinin suresini sisirir (yuk testi ayri
/// bir komut).
///
/// Tekrarlar vaka vaka degil TUR tur: once her soru bir kez, sonra ikinci kez…
/// Boylece ayni sorunun tekrarlari arasinda baska sorular giriyor ve onbellek
/// ya da "az once ayni seyi sordu" etkisi tutarlilik rakamini sisirmiyor.
/// </summary>
public sealed class EvalRunner(GrafirioApi api, EvalOptions options)
{
    public async Task<ScenarioRunDocument> RunAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var cases = options.Suite.Cases
            .Where(c => options.CaseFilter is null || c.Name.Contains(options.CaseFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var attempts = cases.ToDictionary(c => c.Name, _ => new List<EvalAttempt>());
        var total = cases.Count * options.Repeat;
        var done = 0;

        for (var round = 1; round <= options.Repeat; round++)
        {
            foreach (var evalCase in cases)
            {
                ct.ThrowIfCancellationRequested();
                var attempt = await AskAsync(evalCase, round, ct);
                attempts[evalCase.Name].Add(attempt);

                done++;
                Console.WriteLine(
                    $"[{done}/{total}] {(attempt.Passed ? "OK  " : "FAIL")} {evalCase.Name} (tur {round}) " +
                    $"{attempt.Status} {attempt.TotalMs:0} ms" +
                    (attempt.Passed ? "" : $" — {attempt.Error ?? string.Join("; ", attempt.Mismatches)}"));
            }
        }

        var scored = cases.Select(c => EvalScoring.ScoreCase(c, attempts[c.Name], options.PassThreshold)).ToList();
        var allAttempts = attempts.Values.SelectMany(a => a).ToList();
        var (commit, branch) = RunOutput.Git();

        return new ScenarioRunDocument
        {
            Kind = "eval",
            Suite = options.Suite.Suite,
            Label = options.Label,
            StartedAt = started,
            FinishedAt = DateTimeOffset.UtcNow,
            GitCommit = commit,
            GitBranch = branch,
            Target = options.BaseUrl,
            Environment = new Dictionary<string, string>
            {
                ["connectionId"] = options.ConnectionId.ToString(),
                ["repeat"] = options.Repeat.ToString(),
                ["passThreshold"] = options.PassThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["pricing"] = options.Pricing is null
                    ? "tanimsiz (yalnizca token)"
                    : $"in={options.Pricing.InputPerMillion} cached={options.Pricing.CachedInputPerMillion} out={options.Pricing.OutputPerMillion} USD/1M",
                ["description"] = options.Suite.Description ?? ""
            },
            Metrics = EvalScoring.Summarize(scored, allAttempts),
            Cases = scored
        };
    }

    private async Task<EvalAttempt> AskAsync(EvalCase evalCase, int round, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var submit = await api.SubmitAsync(options.ConnectionId, evalCase.Question, ct);

            string status;
            ResultResponse? result = null;

            if (submit.NeedsClarification)
            {
                status = "clarification";
            }
            else if (submit.QueryId is null || submit.HttpStatus >= 400)
            {
                return Finish(evalCase, round, timer, "error", submit.Usage, submit.PreparationMs,
                    error: $"HTTP {submit.HttpStatus}: {submit.Error}");
            }
            else
            {
                status = await api.WaitAsync(submit.QueryId.Value, options.QuestionTimeout, options.PollInterval, ct);
                if (status == "completed") result = await api.GetResultAsync(submit.QueryId.Value, ct);
            }

            timer.Stop();
            return Finish(evalCase, round, timer, status, submit.Usage ?? result?.Usage, submit.PreparationMs,
                submit.QueryId, result);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                              && !ct.IsCancellationRequested)
        {
            return Finish(evalCase, round, timer, "error", null, null, error: exception.Message);
        }
    }

    private EvalAttempt Finish(EvalCase evalCase, int round, Stopwatch timer, string status, TokenUsage? usage,
        int? preparationMs, Guid? queryId = null, ResultResponse? result = null, string? error = null)
    {
        var mismatches = new List<string>();
        var passed = error is null && EvalScoring.Evaluate(evalCase.Expect, status, result?.Parameters, mismatches);

        return new EvalAttempt
        {
            Index = round,
            Status = status,
            Passed = passed,
            TotalMs = timer.Elapsed.TotalMilliseconds,
            PreparationMs = preparationMs,
            Usage = usage,
            CostUsd = usage is not null && options.Pricing is not null ? options.Pricing.Cost(usage) : null,
            ParamsFingerprint = result?.Parameters is null ? null : JsonMatch.Fingerprint(result.Parameters),
            ResultFingerprint = result?.Result is null
                ? null
                : JsonMatch.Fingerprint(result.Result, EvalScoring.IsVolatileResultKey),
            Mismatches = mismatches,
            Error = error ?? (status is "failed" ? result?.Error : null),
            QueryId = queryId,
            Parameters = result?.Parameters
        };
    }
}

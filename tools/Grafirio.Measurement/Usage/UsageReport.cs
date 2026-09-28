using System.Globalization;
using Grafirio.Measurement.Evaluation;
using Grafirio.Measurement.Reporting;
using Npgsql;

namespace Grafirio.Measurement.Usage;

/// <summary>Bir soru kaydinin rapor icin gereken kismi.</summary>
public sealed record QuestionRow(
    Guid ConfigId,
    string UserId,
    string Question,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    int? PreparationMs,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    short? FeedbackRating);

/// <summary>Bir "Analiz Et" kaydinin rapor icin gereken kismi.</summary>
public sealed record AnalysisRow(
    string Status,
    DateTime CreatedAt,
    int? DurationMs,
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens);

public enum UsageBucket { Day, Week }

/// <summary>
/// Gercek kullanimdan uretilen rapor: sistem ne kadar kullaniliyor, kullanicilar
/// ne kadar sure bekliyor, ne kadar tutuyor ve cevaplardan memnunlar mi.
///
/// Cikti yalnizca TOPLU rakamlar: kullanici kimligi, sirket ve soru metni
/// belgeye girmiyor. Soru metni yalnizca "ayni soru tekrar soruldu mu"
/// karsilastirmasi icin bellekte kullaniliyor.
/// </summary>
public static class UsageReport
{
    /// <summary>
    /// Ayni kullanicinin ayni soruyu bu sure icinde yeniden sormasi "tekrar"
    /// sayiliyor: ilk cevaptan memnun kalmamanin en guvenilir dolayli isareti.
    /// </summary>
    public static readonly TimeSpan ReaskWindow = TimeSpan.FromHours(24);

    public static async Task<(List<QuestionRow> Questions, List<AnalysisRow> Analyses)> LoadAsync(
        string connectionString, DateTime since, DateTime until, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var questions = new List<QuestionRow>();
        try
        {
            await using (var command = new NpgsqlCommand("""
                SELECT q."ConfigId", q."UserId", q."Question", q."Status", q."CreatedAt", q."CompletedAt",
                       q."PreparationMs", q."LlmInputTokens", q."LlmCachedInputTokens", q."LlmOutputTokens",
                       q."FeedbackRating"
                FROM "QueryHistories" q
                WHERE q."CreatedAt" >= @since AND q."CreatedAt" < @until
                """, connection))
            {
                command.Parameters.AddWithValue("since", since);
                command.Parameters.AddWithValue("until", until);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    questions.Add(new QuestionRow(
                        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                        reader.GetDateTime(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                        NullableInt(reader, 6), NullableInt(reader, 7), NullableInt(reader, 8), NullableInt(reader, 9),
                        reader.IsDBNull(10) ? null : reader.GetInt16(10)));
                }
            }

            var analyses = new List<AnalysisRow>();
            await using (var command = new NpgsqlCommand("""
                SELECT a."Status", a."CreatedAt", a."AnalysisDurationMs", a."LlmInputTokens",
                       a."LlmCachedInputTokens", a."LlmOutputTokens"
                FROM "AnalysisConfigs" a
                WHERE a."CreatedAt" >= @since AND a."CreatedAt" < @until
                """, connection))
            {
                command.Parameters.AddWithValue("since", since);
                command.Parameters.AddWithValue("until", until);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    analyses.Add(new AnalysisRow(reader.GetString(0), reader.GetDateTime(1), NullableInt(reader, 2),
                        NullableInt(reader, 3), NullableInt(reader, 4), NullableInt(reader, 5)));
                }
            }

            return (questions, analyses);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            throw new InvalidOperationException(
                "Olcum kolonlari veritabaninda yok. DataAnalysis servisi bu surumle en az bir kez " +
                "baslatilmali (SchemaPatches kolonlari acilista ekliyor). Ayrinti: " + exception.MessageText);
        }
    }

    private static int? NullableInt(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static ScenarioRunDocument Build(IReadOnlyList<QuestionRow> questions, IReadOnlyList<AnalysisRow> analyses,
        DateTime since, DateTime until, UsageBucket bucket, LlmPricing? pricing, string target, string? label)
    {
        var reasks = FindReasks(questions);

        var cases = questions
            .GroupBy(q => BucketOf(q.CreatedAt, bucket))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Case
            {
                Name = g.Key,
                Group = bucket == UsageBucket.Day ? "gun" : "hafta",
                // Bir donem "basarili": sorularin cogu cevaplanmis ve kullanicilar
                // cevaplardan cogunlukla memnun. Esikler kaba; asil deger metriklerde.
                Success = CompletedRate(g.ToList()) >= 0.8,
                Metrics = QuestionMetrics(g.ToList(), reasks, pricing)
                    .Concat(AnalysisMetrics(analyses.Where(a => BucketOf(a.CreatedAt, bucket) == g.Key).ToList(), pricing))
                    .ToList()
            })
            .ToList();

        var (commit, branch) = RunOutput.Git();
        var metrics = QuestionMetrics(questions, reasks, pricing).Concat(AnalysisMetrics(analyses, pricing)).ToList();

        return new ScenarioRunDocument
        {
            Kind = "usage",
            Suite = "grafirio-usage",
            Label = label ?? $"{since:yyyy-MM-dd} – {until.AddDays(-1):yyyy-MM-dd}",
            StartedAt = new DateTimeOffset(since, TimeSpan.Zero),
            FinishedAt = new DateTimeOffset(until, TimeSpan.Zero),
            GitCommit = commit,
            GitBranch = branch,
            Target = target,
            Environment = new Dictionary<string, string>
            {
                ["period"] = $"{since:O} – {until:O}",
                ["bucket"] = bucket.ToString().ToLowerInvariant(),
                ["reaskWindow"] = $"{ReaskWindow.TotalHours:0} saat",
                ["pricing"] = pricing is null ? "tanimsiz (yalnizca token)" : "USD/1M token"
            },
            Metrics = metrics,
            Cases = cases
        };
    }

    private static IEnumerable<Metric> QuestionMetrics(IReadOnlyList<QuestionRow> rows, ISet<QuestionRow> reasks,
        LlmPricing? pricing)
    {
        var metrics = new List<Metric> { Metric.Info("questions", rows.Count, "count") };
        if (rows.Count == 0) return metrics;

        metrics.Add(Metric.Info("users.active", rows.Select(r => r.UserId).Distinct().Count(), "count"));
        metrics.Add(Metric.Higher("completed.rate", CompletedRate(rows), "ratio"));
        metrics.Add(Metric.Info("clarification.rate", rows.Count(r => r.Status == "clarification") / (double)rows.Count, "ratio"));
        metrics.Add(Metric.Lower("failed.rate", rows.Count(r => r.Status == "failed") / (double)rows.Count, "ratio"));
        metrics.Add(Metric.Lower("reask.rate", rows.Count(reasks.Contains) / (double)rows.Count, "ratio"));

        var rated = rows.Where(r => r.FeedbackRating is not null).ToList();
        metrics.Add(Metric.Info("feedback.count", rated.Count, "count"));
        if (rated.Count > 0)
            metrics.Add(Metric.Higher("feedback.positive.rate", rated.Count(r => r.FeedbackRating > 0) / (double)rated.Count, "ratio"));

        // Kullanicinin bekledigi sure: hazirlik (ceviri) + satir olusturmadan bitise.
        var waits = rows
            .Where(r => r.Status == "completed" && r.CompletedAt is not null)
            .Select(r => (r.CompletedAt!.Value - r.CreatedAt).TotalMilliseconds + (r.PreparationMs ?? 0))
            .ToList();
        metrics.AddIfFinite(Metric.Lower("latency.p50", Stats.Percentile(waits, 50), "ms"));
        metrics.AddIfFinite(Metric.Lower("latency.p95", Stats.Percentile(waits, 95), "ms"));

        var metered = rows.Where(r => r.InputTokens is not null).ToList();
        if (metered.Count > 0)
        {
            metrics.Add(Metric.Lower("tokens.per_question",
                metered.Average(r => (r.InputTokens ?? 0) + (r.OutputTokens ?? 0)), "tokens"));
            if (pricing is not null)
            {
                var costs = metered.Select(r => pricing.Cost(ToUsage(r.InputTokens, r.CachedInputTokens, r.OutputTokens))).ToList();
                metrics.Add(Metric.Lower("cost.questions", costs.Sum(), "USD"));
                metrics.Add(Metric.Lower("cost.per_question", costs.Average(), "USD"));
            }
        }

        return metrics;
    }

    private static IEnumerable<Metric> AnalysisMetrics(IReadOnlyList<AnalysisRow> rows, LlmPricing? pricing)
    {
        var metrics = new List<Metric> { Metric.Info("analyses", rows.Count, "count") };
        if (rows.Count == 0) return metrics;

        // "awaiting_answers" basarisizlik degil: sozluk hazir, kullanicidan cevap bekliyor.
        metrics.Add(Metric.Higher("analysis.success.rate",
            rows.Count(r => r.Status is "ready" or "awaiting_answers") / (double)rows.Count, "ratio"));

        var durations = rows.Where(r => r.DurationMs is not null).Select(r => (double)r.DurationMs!.Value).ToList();
        metrics.AddIfFinite(Metric.Lower("analysis.duration.p50", Stats.Percentile(durations, 50), "ms"));
        metrics.AddIfFinite(Metric.Lower("analysis.duration.p95", Stats.Percentile(durations, 95), "ms"));

        var metered = rows.Where(r => r.InputTokens is not null).ToList();
        if (metered.Count > 0)
        {
            metrics.Add(Metric.Lower("analysis.tokens.mean",
                metered.Average(r => (r.InputTokens ?? 0) + (r.OutputTokens ?? 0)), "tokens"));
            if (pricing is not null)
                metrics.Add(Metric.Lower("cost.analyses", metered
                    .Sum(r => pricing.Cost(ToUsage(r.InputTokens, r.CachedInputTokens, r.OutputTokens))), "USD"));
        }

        return metrics;
    }

    /// <summary>
    /// Ayni kullanici + ayni analiz + ayni (normalize) soru metni, onceki
    /// sorudan <see cref="ReaskWindow"/> icinde. Ilk soru sayilmiyor, yalnizca
    /// tekrarlar.
    /// </summary>
    public static HashSet<QuestionRow> FindReasks(IReadOnlyList<QuestionRow> rows)
    {
        var result = new HashSet<QuestionRow>();
        foreach (var group in rows.GroupBy(r => (r.UserId, r.ConfigId, Text: NormalizeQuestion(r.Question))))
        {
            QuestionRow? previous = null;
            foreach (var row in group.OrderBy(r => r.CreatedAt))
            {
                if (previous is not null && row.CreatedAt - previous.CreatedAt <= ReaskWindow) result.Add(row);
                previous = row;
            }
        }

        return result;
    }

    private static string NormalizeQuestion(string question) =>
        string.Join(' ', question.ToLowerInvariant()
            .Split(default(char[]), StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd('?', '.', '!', ' ');

    private static double CompletedRate(IReadOnlyList<QuestionRow> rows) =>
        rows.Count == 0 ? 0 : rows.Count(r => r.Status == "completed") / (double)rows.Count;

    private static TokenUsage ToUsage(int? input, int? cached, int? output) =>
        new(1, input ?? 0, cached ?? 0, output ?? 0, 0, 0);

    public static string BucketOf(DateTime value, UsageBucket bucket) => bucket == UsageBucket.Day
        ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : $"{ISOWeek.GetYear(value)}-W{ISOWeek.GetWeekOfYear(value):00}";
}

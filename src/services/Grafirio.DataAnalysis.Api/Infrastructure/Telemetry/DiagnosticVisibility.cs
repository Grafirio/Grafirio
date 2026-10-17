using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Features.Admin;

namespace Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;

public static class DiagnosticVisibility
{
    public sealed record Latency(int SampleCount, int MissingCount, double? AverageMs, int? P95Ms);
    public sealed record Period(int Total, int Completed, int Failed, int EmptyResults, double? SuccessRate, Latency Latency);

    // A missing completion timestamp cannot provide a reliable terminal duration.
    public static Latency MeasureLatency(IEnumerable<DiagnosticEndpoints.View> rows)
    {
        var terminal = rows.Where(r => DiagnosticMetrics.IsTerminal(r.Status)).ToList();
        var values = terminal.Where(r => r.CompletedAt.HasValue && r.DurationMs >= 0).Select(r => r.DurationMs).Order().ToArray();
        return new(values.Length, terminal.Count - values.Length,
            values.Length == 0 ? null : values.Average(),
            values.Length == 0 ? null : values[(int)Math.Ceiling(values.Length * .95) - 1]);
    }

    public static Period MeasurePeriod(List<DiagnosticEndpoints.View> rows)
    {
        var completed = rows.Count(r => r.Status == "completed");
        var failed = rows.Count(r => r.Status == "failed");
        return new(rows.Count, completed, failed, rows.Count(r => DiagnosticMetrics.IsEmptyResult(r.Status, r.ErrorCode)),
            DiagnosticMetrics.SuccessRate(completed, failed), MeasureLatency(rows));
    }

    private static string[] Reasons(DiagnosticEndpoints.View row, DiagnosticSettings settings)
    {
        var reasons = new List<string>();
        if (row.Status == "failed") reasons.Add("failed");
        if (row.FeedbackRating == -1) reasons.Add("negative_feedback");
        if (row.Status == "clarification") reasons.Add("clarification");
        if (row.DurationMs > settings.SlowQuerySeconds * 1000) reasons.Add("slow");
        return reasons.ToArray();
    }

    public static object Build(List<DiagnosticEndpoints.View> rows, DiagnosticSettings settings)
    {
        var candidates = rows.Where(r => r.Legacy || r.ReviewStatus is not ("fixed" or "verified"))
            .Select(r => new { r.Id, r.Question, r.CompanyId, r.ConnectionId, r.Status, r.ReviewStatus,
                r.DurationMs, reasons = Reasons(r, settings) }).Where(r => r.reasons.Length > 0)
            .OrderByDescending(r => r.reasons.Contains("failed")).ThenByDescending(r => r.reasons.Length)
            .ThenByDescending(r => r.DurationMs).ThenBy(r => r.Id).ToList();
        return new {
            latency = MeasureLatency(rows),
            executionJobsCreated = rows.Count(r => r.QueryId.HasValue),
            statusDistribution = rows.GroupBy(r => r.Status).OrderBy(g => g.Key)
                .Select(g => new { status = g.Key, group = DiagnosticMetrics.Group(g.Key), count = g.Count() }).ToArray(),
            byCompany = rows.GroupBy(r => r.CompanyId).Select(g => new { companyId = g.Key, metrics = MeasurePeriod(g.ToList()) })
                .OrderByDescending(g => g.metrics.Failed).ThenByDescending(g => g.metrics.Total).ThenBy(g => g.companyId).ToArray(),
            byConnection = rows.GroupBy(r => new { r.CompanyId, r.ConnectionId })
                .Select(g => new { g.Key.CompanyId, g.Key.ConnectionId, metrics = MeasurePeriod(g.ToList()) })
                .OrderByDescending(g => g.metrics.Failed).ThenByDescending(g => g.metrics.Total).ThenBy(g => g.ConnectionId).ToArray(),
            reviewCandidateCount = candidates.Count, reviewCandidateLimit = 20, reviewCandidates = candidates.Take(20).ToArray(),
            legacyCount = rows.Count(r => r.Legacy)
        };
    }

    public static object Compare(List<DiagnosticEndpoints.View> current, List<DiagnosticEndpoints.View> previous,
        DateTime capturedAt, int days, int retentionDays, bool trackingLookup)
    {
        var span = Math.Clamp(days, 1, 365);
        var start = capturedAt.AddDays(-span);
        var a = MeasurePeriod(current); var b = MeasurePeriod(previous);
        return new {
            available = !trackingLookup, reason = trackingLookup ? "tracking_lookup" : null,
            currentFrom = start, currentUntil = capturedAt, previousFrom = start.AddDays(-span), previousUntil = start,
            current = a, previous = trackingLookup ? null : b,
            successRateChangePoints = !trackingLookup && a.SuccessRate.HasValue && b.SuccessRate.HasValue
                ? (a.SuccessRate.Value - b.SuccessRate.Value) * 100 : (double?)null,
            averageChangeMs = !trackingLookup && a.Latency.AverageMs.HasValue && b.Latency.AverageMs.HasValue
                ? a.Latency.AverageMs.Value - b.Latency.AverageMs.Value : (double?)null,
            p95ChangeMs = !trackingLookup && a.Latency.P95Ms.HasValue && b.Latency.P95Ms.HasValue
                ? a.Latency.P95Ms.Value - b.Latency.P95Ms.Value : (int?)null,
            retentionMayLimitHistory = span * 2 > retentionDays,
            legacyCount = current.Concat(previous).Count(r => r.Legacy)
        };
    }
}

using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Grafirio.Telemetry;

public sealed record MetricSample(string Name, string Meter, string? Unit, string Kind,
    Dictionary<string, string> Tags, double? Value, long? Count, double? Sum,
    double? Min, double? Max);

public sealed record LogSample(DateTime Timestamp, string Level, string Category,
    string Message, string? ExceptionType, string? TraceId, string? SpanId);

public sealed record TraceSample(DateTime Timestamp, string Name, string Kind, string Status,
    double DurationMs, string TraceId, string SpanId, string? ParentSpanId,
    Dictionary<string, string> Tags);

public sealed class ObservabilitySnapshot(string serviceName, string environment)
{
    public TelemetryArchive? Archive { get; set; }
    public const string Policy = "GrafirioObservabilityAdmin";
    public const int EventCapacity = 1000;
    public const int MetricCapacity = 1000;
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);
    private readonly object gate = new();
    private readonly Queue<LogSample> logs = new();
    private readonly Queue<TraceSample> traces = new();
    private readonly DateTime startedAt = DateTime.UtcNow;
    private readonly string instanceId = Guid.NewGuid().ToString("N");
    public string InstanceId => instanceId;
    private MetricSample[] metrics = [];
    private DateTime? metricsCollectedAt;
    private long droppedLogs;
    private long droppedTraces;
    private int omittedMetrics;

    public void SetMetrics(IEnumerable<MetricSample> samples, int omitted = 0)
    {
        var bounded = samples.Take(MetricCapacity + 1).ToArray();
        lock (gate)
        {
            metrics = bounded.Take(MetricCapacity).ToArray();
            omittedMetrics = omitted + Math.Max(0, bounded.Length - MetricCapacity);
            metricsCollectedAt = DateTime.UtcNow;
            foreach (var metric in metrics) Archive?.Record(new ArchiveEvent { Kind = "metric", Name = "metric",
                Timestamp = metricsCollectedAt.Value, Metric = new(metric.Name, metric.Unit, metric.Kind, metric.Value, metric.Count, metric.Sum, metric.Min, metric.Max, metric.Tags) });
        }
    }

    public void AddLog(LogSample sample)
    {
        lock (gate)
        {
            TrimExpired();
            if (logs.Count == EventCapacity) { logs.Dequeue(); droppedLogs++; }
            logs.Enqueue(sample);
            Archive?.Record(new ArchiveEvent { Timestamp = sample.Timestamp, Level = sample.Level,
                Category = sample.Category, ExceptionType = sample.ExceptionType, TraceId = sample.TraceId, SpanId = sample.SpanId });
        }
    }

    public void AddTrace(TraceSample sample)
    {
        lock (gate)
        {
            TrimExpired();
            if (traces.Count == EventCapacity) { traces.Dequeue(); droppedTraces++; }
            traces.Enqueue(sample);
            Archive?.Record(new ArchiveEvent { Kind = "trace", Name = "activity", Timestamp = sample.Timestamp,
                TraceId = sample.TraceId, SpanId = sample.SpanId, ParentSpanId = sample.ParentSpanId,
                Level = sample.Status == "Error" ? "Error" : "Information", ExceptionType = sample.Tags.GetValueOrDefault("error.type"),
                Status = sample.Status, DurationMs = sample.DurationMs,
                QueryId = sample.Tags.GetValueOrDefault("grafirio.query_id"), DiagnosticId = sample.Tags.GetValueOrDefault("grafirio.diagnostic.id") });
        }
    }

    public object Read(int minutes = 15, int limit = 100, string? level = null, string? traceId = null)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Clamp(minutes, 1, 30));
        limit = Math.Clamp(limit, 1, 200);
        using var process = Process.GetCurrentProcess();
        lock (gate)
        {
            TrimExpired();
            var filteredLogs = logs.Where(sample => sample.Timestamp >= cutoff
                && (string.IsNullOrEmpty(level) || sample.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(traceId) || sample.TraceId == traceId)).ToArray();
            var filteredTraces = traces.Where(sample => sample.Timestamp >= cutoff
                && (string.IsNullOrEmpty(traceId) || sample.TraceId == traceId)).ToArray();
            return new
            {
                service = serviceName, environment, instanceId, version = GrafirioTelemetry.Version,
                startedAt, capturedAt = DateTime.UtcNow, metricsCollectedAt,
                retentionMinutes = Retention.TotalMinutes, eventCapacity = EventCapacity,
                droppedLogs, droppedTraces, omittedMetrics,
                archive = Archive?.Status,
                runtime = new
                {
                    uptimeSeconds = (DateTime.UtcNow - startedAt).TotalSeconds,
                    workingSetBytes = process.WorkingSet64,
                    managedHeapBytes = GC.GetTotalMemory(false),
                    cpuSeconds = process.TotalProcessorTime.TotalSeconds,
                    threadCount = process.Threads.Count
                },
                metrics,
                logs = filteredLogs.Reverse().Take(limit).ToArray(),
                traces = filteredTraces.Reverse().Take(limit).ToArray(),
                matchingLogs = filteredLogs.Length, matchingTraces = filteredTraces.Length
            };
        }
    }

    private void TrimExpired()
    {
        var cutoff = DateTime.UtcNow - Retention;
        while (logs.TryPeek(out var log) && log.Timestamp < cutoff) logs.Dequeue();
        while (traces.TryPeek(out var trace) && trace.Timestamp < cutoff) traces.Dequeue();
    }
}

public static class ObservabilityEndpoints
{
    public static IEndpointConventionBuilder MapGrafirioObservability(this IEndpointRouteBuilder endpoints,
        string pattern = "/api/admin/observability") => endpoints.MapGet(pattern,
        (HttpContext context, ObservabilitySnapshot snapshot, int? minutes, int? limit, string? level, string? traceId) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(snapshot.Read(minutes ?? 15, limit ?? 100, level, traceId));
        }).RequireAuthorization(ObservabilitySnapshot.Policy).WithTags("Admin");
}

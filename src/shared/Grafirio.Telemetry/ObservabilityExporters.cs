using System.Diagnostics;
using System.Globalization;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace Grafirio.Telemetry;

internal static class ObservabilityPrivacy
{
    private static readonly HashSet<string> AllowedTags =
    [
        "http.request.method", "http.response.status_code", "http.route", "error.type",
        "network.protocol.version", "url.scheme", "server.port", "db.system", "db.system.name",
        "llm.operation", "llm.outcome", "llm.token.type", "gen_ai.request.model",
        "datasource.route", "datasource.method", "datasource.outcome", "question.outcome",
        "query.status", "analysis.outcome", "feedback.rating", "chart.model", "chart.user",
        "gc.heap.generation", "gc.generation", "gc.reason", "gc.type", "thread.state",
        "aspnetcore.request.is_unhandled", "aspnetcore.routing.is_fallback", "otel.metric.overflow"
    ];

    public static string Clip(string? value, int length = 256) =>
        value is null ? "" : value[..Math.Min(value.Length, length)];

    public static void AddTag(Dictionary<string, string> tags, string key, object? value)
    {
        if (AllowedTags.Contains(key))
            tags[key] = Clip(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    public static bool IsNoise(string? path) => path is not null &&
        (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
         || path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase)
         || path.Contains("/admin/observability", StringComparison.OrdinalIgnoreCase));
}

public sealed class SnapshotMetricExporter(ObservabilitySnapshot snapshot) : BaseExporter<Metric>
{
    public override ExportResult Export(in Batch<Metric> batch)
    {
        var samples = new List<MetricSample>();
        var omitted = 0;
        foreach (var metric in batch)
        {
            foreach (ref readonly var point in metric.GetMetricPoints())
            {
                if (samples.Count == ObservabilitySnapshot.MetricCapacity) { omitted++; continue; }
                var tags = new Dictionary<string, string>();
                foreach (var tag in point.Tags) ObservabilityPrivacy.AddTag(tags, tag.Key, tag.Value);
                double? value = null, sum = null, min = null, max = null;
                long? count = null;
                var kind = metric.MetricType.ToString();
                if (metric.MetricType.IsHistogram())
                {
                    count = point.GetHistogramCount();
                    sum = Finite(point.GetHistogramSum());
                    if (point.TryGetHistogramMinMaxValues(out var minimum, out var maximum))
                    {
                        min = Finite(minimum);
                        max = Finite(maximum);
                    }
                }
                else if (metric.MetricType.IsSum())
                    value = Finite(metric.MetricType.IsLong() ? point.GetSumLong() : point.GetSumDouble());
                else if (metric.MetricType.IsGauge())
                    value = Finite(metric.MetricType.IsLong() ? point.GetGaugeLastValueLong() : point.GetGaugeLastValueDouble());
                samples.Add(new MetricSample(metric.Name, metric.MeterName, metric.Unit, kind,
                    tags, value, count, sum, min, max));
            }
        }
        snapshot.SetMetrics(samples, omitted);
        return ExportResult.Success;
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
}

public sealed class SnapshotLogExporter(ObservabilitySnapshot snapshot) : BaseExporter<LogRecord>
{
    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (var record in batch)
        {
            string? template = null;
            if (record.Attributes is not null)
                foreach (var attribute in record.Attributes)
                    if (attribute.Key == "{OriginalFormat}") template = attribute.Value as string;
            snapshot.AddLog(new LogSample(record.Timestamp, record.LogLevel.ToString(),
                ObservabilityPrivacy.Clip(record.CategoryName),
                string.IsNullOrEmpty(template) || !template.Contains('{')
                    ? "Log mesajı gizlendi (yapılandırılmış şablon yok)."
                    : ObservabilityPrivacy.Clip(template, 1000),
                record.Exception?.GetType().Name,
                record.TraceId == default ? null : record.TraceId.ToHexString(),
                record.SpanId == default ? null : record.SpanId.ToHexString()));
        }
        return ExportResult.Success;
    }
}

public sealed class SnapshotTraceExporter(ObservabilitySnapshot snapshot) : BaseExporter<Activity>
{
    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            var url = activity.GetTagItem("url.full") as string ?? activity.GetTagItem("http.url") as string;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && ObservabilityPrivacy.IsNoise(uri.AbsolutePath)) continue;
            if (ObservabilityPrivacy.IsNoise(activity.GetTagItem("url.path") as string)
                || ObservabilityPrivacy.IsNoise(activity.GetTagItem("http.route") as string)) continue;
            var tags = new Dictionary<string, string>();
            foreach (var tag in activity.TagObjects) ObservabilityPrivacy.AddTag(tags, tag.Key, tag.Value);
            snapshot.AddTrace(new TraceSample(activity.StartTimeUtc,
                ObservabilityPrivacy.Clip(activity.OperationName), activity.Kind.ToString(), activity.Status.ToString(),
                activity.Duration.TotalMilliseconds, activity.TraceId.ToHexString(), activity.SpanId.ToHexString(),
                activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(), tags));
        }
        return ExportResult.Success;
    }
}

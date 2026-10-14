using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Microsoft.AspNetCore.Http;

namespace Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;

public static class QueryDiagnostics
{
    public const string PromptVersion = "question-translation-v3-metric-rules";
    private static readonly Regex PrivateText = new(@"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|(?<!\d)\d{11}(?!\d)", RegexOptions.Compiled);
    public static string Mask(string? text) => PrivateText.Replace(text ?? "", "[gizlendi]");
    public static string SafeText(string? text)
    {
        var safe = Mask(text);
        return Regex.Replace(safe, "(?i)(\\\"?(?:password|secret|api.?key|authorization|token)\\\"?\\s*[:=]\\s*)(\\\"[^\\\"]*\\\"|[^\\s,}]+)", "$1\"[gizlendi]\"");
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static JsonNode? Parse(string? value)
    {
        try { return string.IsNullOrWhiteSpace(value) ? null : JsonNode.Parse(value); }
        catch (JsonException) { return null; }
    }

    public static string SafeJson(string value)
    {
        var node = Parse(value);
        if (node is null) return "{}";
        Scrub(node);
        return node.ToJsonString();
    }

    private static void Scrub(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToList())
            {
                if (Regex.IsMatch(pair.Key, "password|secret|api.?key|credential|authorization|^(access|refresh|api)?token$", RegexOptions.IgnoreCase))
                    obj[pair.Key] = "[gizlendi]";
                else if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text)) obj[pair.Key] = Mask(text);
                else if (pair.Value is not null) Scrub(pair.Value);
            }
        else if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Mask(text);
                else if (array[i] is { } child) Scrub(child);
    }

    public static void Step(QueryDiagnostic? row, string stage)
    {
        if (row is null) return;
        row.Stage = stage;
        var events = Parse(row.EventsJson) as JsonArray ?? [];
        events.Add(new JsonObject { ["stage"] = stage, ["at"] = DateTime.UtcNow });
        row.EventsJson = events.ToJsonString();
    }

    public static void Finish(QueryDiagnostic row, IResult? result, Exception? exception, string outcome, int duration)
    {
        // The worker can finish during submit. HTTP preparation must not overwrite its terminal status.
        if (outcome == "submitted" && exception is null && row.Stage.StartsWith("execution_", StringComparison.Ordinal))
        {
            row.DurationMs = Math.Max(row.DurationMs, duration);
            if (row.Status is "processing" or "queued" or "cancelling") row.CompletedAt = null;
            return;
        }
        row.DurationMs = duration;
        row.CompletedAt = DateTime.UtcNow;
        var status = (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        row.Status = exception is not null ? "failed" : outcome == "submitted" ? "submitted"
            : outcome == "clarification" ? "clarification" : status >= 400 ? "failed" : outcome;
        var payload = (result as IValueHttpResult)?.Value;
        var json = payload is null ? null : JsonSerializer.SerializeToNode(payload);
        row.Error = SafeText(exception?.Message ?? json?["error"]?.ToString() ?? json?["detail"]?.ToString());
        if (row.Status == "failed") row.ErrorCode ??= Classify(row.Error, row.Stage);
        Step(row, row.Stage);
    }

    public static string Classify(string? error, string stage = "execution")
    {
        var text = (error ?? "").ToLowerInvariant();
        if (text.Contains("kayıt bulunamadı") || text.Contains("veri bulunamadı")) return "empty_result";
        if (text.Contains("yetersiz") || text.Contains("at least") || text.Contains("insufficient")) return "insufficient_data";
        if (text.Contains("duration") || text.Contains("timeout") || text.Contains("timed out")) return "timeout";
        if (text.Contains("scope is invalid") || text.Contains("json") || text.Contains("invalid analysis parameter")) return "invalid_parameters";
        if (text.Contains("scope") || text.Contains("selection") || text.Contains("sözlüğü") || text.Contains("dictionary")) return "scope_mismatch";
        return stage == "translation" ? "translation_failed" : stage is "received" or "validation" ? "request_rejected" : "execution_failed";
    }

    // Subset comparison allows expectations to focus on meaning rather than chart wording.
    public static IReadOnlyList<string> Differences(JsonNode? expected, JsonNode? actual, string path = "")
    {
        var errors = new List<string>();
        if (expected is JsonObject obj)
            foreach (var pair in obj)
                errors.AddRange(Differences(pair.Value, (actual as JsonObject)?[pair.Key], path.Length == 0 ? pair.Key : $"{path}.{pair.Key}"));
        else if (!JsonNode.DeepEquals(expected, actual)) errors.Add(path);
        return errors;
    }
}

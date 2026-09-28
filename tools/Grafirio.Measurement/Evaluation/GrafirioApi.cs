using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grafirio.Measurement.Evaluation;

/// <summary>
/// Soru-cevap akisinin HTTP istemcisi — kullanicinin arayuzden yaptigi cagrilarin
/// aynisi: soruyu gonder, durum son bulana kadar yokla, sonucu oku.
/// </summary>
public sealed class GrafirioApi(HttpClient http, string prefix)
{
    public static GrafirioApi Create(string baseUrl, string prefix, string token, TimeSpan timeout)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = timeout };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new GrafirioApi(http, prefix.Trim('/'));
    }

    private string Path(string relative) => prefix.Length == 0 ? relative : $"{prefix}/{relative}";

    public async Task<SubmitResponse> SubmitAsync(Guid connectionId, string question, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(Path("api/agent/query"),
            new { connectionId, question }, ct);
        var body = await ReadJsonAsync(response, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(
                $"Kimlik reddedildi (HTTP {(int)response.StatusCode}). Token'in suresi dolmus olabilir.");

        return new SubmitResponse(
            (int)response.StatusCode,
            Guid.TryParse(Json.Str(body?["queryId"]), out var id) ? id : null,
            Json.Str(body?["status"]),
            Json.Bool(body?["needsClarification"]),
            body?["error"]?.ToString() ?? body?["detail"]?.ToString(),
            TokenUsage.From(body?["usage"]),
            Json.Int(body?["preparationMs"]));
    }

    /// <summary>Durum son bulana kadar yoklar; son durumu doner.</summary>
    public async Task<string> WaitAsync(Guid queryId, TimeSpan timeout, TimeSpan interval, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            using var response = await http.GetAsync(Path($"api/agent/query/{queryId}/status"), ct);
            var body = await ReadJsonAsync(response, ct);
            var status = Json.Str(body?["status"]) ?? $"http_{(int)response.StatusCode}";

            if (status is not ("processing" or "queued" or "pending" or "cancelling")) return status;
            if (deadline.Elapsed > timeout) return "timeout";

            await Task.Delay(interval, ct);
        }
    }

    public async Task<ResultResponse> GetResultAsync(Guid queryId, CancellationToken ct)
    {
        using var response = await http.GetAsync(Path($"api/agent/query/{queryId}/result"), ct);
        var body = await ReadJsonAsync(response, ct);
        return new ResultResponse(
            Json.Str(body?["status"]),
            body?["llmParameters"],
            body?["result"],
            Json.Int(body?["durationMs"]),
            TokenUsage.From(body?["usage"]),
            body?["result"]?["error"]?.ToString());
    }

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); }
        catch (System.Text.Json.JsonException) { return new JsonObject { ["error"] = text.Length > 500 ? text[..500] : text }; }
    }
}

public sealed record SubmitResponse(
    int HttpStatus, Guid? QueryId, string? Status, bool NeedsClarification, string? Error, TokenUsage? Usage,
    int? PreparationMs);

public sealed record ResultResponse(
    string? Status, JsonNode? Parameters, JsonNode? Result, int? DurationMs, TokenUsage? Usage, string? Error);

public sealed record TokenUsage(int Calls, int InputTokens, int CachedInputTokens, int OutputTokens, int ReasoningTokens,
    int DurationMs)
{
    public static TokenUsage? From(JsonNode? node) => node is JsonObject o
        ? new TokenUsage(
            Json.Int(o["calls"]) ?? 0,
            Json.Int(o["inputTokens"]) ?? 0,
            Json.Int(o["cachedInputTokens"]) ?? 0,
            Json.Int(o["outputTokens"]) ?? 0,
            Json.Int(o["reasoningTokens"]) ?? 0,
            Json.Int(o["durationMs"]) ?? 0)
        : null;
}

/// <summary>USD / 1M token. Tanimsizsa maliyet hesaplanmiyor, yalnizca token raporlaniyor.</summary>
public sealed record LlmPricing(double InputPerMillion, double CachedInputPerMillion, double OutputPerMillion)
{
    public double Cost(TokenUsage usage) =>
        ((usage.InputTokens - usage.CachedInputTokens) * InputPerMillion
         + usage.CachedInputTokens * CachedInputPerMillion
         + usage.OutputTokens * OutputPerMillion) / 1_000_000d;
}

/// <summary>
/// Cevaptan guvenli okuma: beklenmedik bir tip (sayi yerine metin) istisna degil
/// null olsun — olcum araci sunucunun bir alanini yanlis okudu diye durmamali.
/// </summary>
internal static class Json
{
    public static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static int? Int(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number
        : node is JsonValue other && other.TryGetValue<double>(out var real) ? (int)real
        : null;

    public static bool Bool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}

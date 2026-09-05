using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Application.Interfaces;

namespace Grafirio.DataAnalysis.Api.Infrastructure.Services;

public sealed class PyCaretClient(HttpClient client, IConfiguration configuration) : IPyCaretClient
{
    public const string ApiKeyHeader = "X-Grafirio-Internal-Key";
    private const string DefaultBaseUrl = "http://pycaret-engine:8002";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private string? ApiKey => configuration["Internal:ApiKey"]
        ?? Environment.GetEnvironmentVariable("INTERNAL__APIKEY");
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public Task<JsonObject> SubmitAsync(PyCaretExecutionContext scope, string configJson,
        string parametersJson, string question, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/agent/analyze", scope, new
        {
            request_id = scope.QueryId.ToString(), query_id = scope.QueryId.ToString(),
            company_id = scope.CompanyId, connection_id = scope.ConnectionId.ToString(),
            config_id = scope.ConfigId.ToString(), config_hash = scope.ConfigHash,
            config_json = configJson, analysis_params_json = parametersJson, user_question = question
        }, ct);

    public Task<JsonObject> GetStatusAsync(PyCaretExecutionContext scope, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, ScopedPath("status", scope), scope, null, ct);

    public Task<JsonObject> GetResultAsync(PyCaretExecutionContext scope, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, ScopedPath("result", scope), scope, null, ct);

    public Task<JsonObject> CancelAsync(PyCaretExecutionContext scope, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, ScopedPath("cancel", scope), scope, null, ct);

    private static string ScopedPath(string operation, PyCaretExecutionContext scope) =>
        $"/agent/analyze/{operation}/{scope.QueryId}?company_id={Uri.EscapeDataString(scope.CompanyId)}" +
        $"&connection_id={scope.ConnectionId}&config_id={scope.ConfigId}&config_hash={scope.ConfigHash}";

    private async Task<JsonObject> SendAsync(HttpMethod method, string path,
        PyCaretExecutionContext scope, object? payload, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new PyCaretClientException("PyCaret internal authentication is not configured.", HttpStatusCode.Unauthorized);

        using var request = new HttpRequestMessage(method,
            (configuration["PyCaret:BaseUrl"] ?? DefaultBaseUrl).TrimEnd('/') + path);
        request.Headers.Add(ApiKeyHeader, ApiKey);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new PyCaretClientException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "PyCaret authentication failed; service unavailable.",
                    HttpStatusCode.NotFound => "PyCaret worker job was not found; submit a new query.",
                    _ => "PyCaret is temporarily unavailable."
                }, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonObject>(timeout.Token);
            if (body is null || !scope.Matches(body))
                throw new PyCaretClientException("PyCaret response execution scope does not match.", HttpStatusCode.Conflict);
            return body;
        }
        catch (HttpRequestException)
        {
            throw new PyCaretClientException("PyCaret is temporarily unreachable.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PyCaretClientException("PyCaret request timed out; status will be checked again.");
        }
        catch (JsonException)
        {
            throw new PyCaretClientException("PyCaret returned an invalid response.");
        }
    }
}
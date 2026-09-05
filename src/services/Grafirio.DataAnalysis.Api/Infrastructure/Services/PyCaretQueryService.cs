using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Application.Interfaces;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Infrastructure.Services;

public sealed class PyCaretQueryService(
    DataAnalysisDbContext db, IPyCaretClient client, IConfiguration configuration,
    ILogger<PyCaretQueryService> logger) : IPyCaretQueryService
{
    private const int DefaultMaxDurationMinutes = 15;
    private const int MaxClarificationLength = 2000;
    public bool IsConfigured => client.IsConfigured;

    public async Task SubmitAsync(QueryHistory query, AnalysisConfig config, CancellationToken ct)
    {
        var scope = RequireScope(query);
        await ExchangeAsync(query, scope,
            () => client.SubmitAsync(scope, config.ConfigJson, query.PyCaretParamsJson, query.Question, ct), ct);
    }

    public async Task RefreshAsync(QueryHistory query, CancellationToken ct)
    {
        if (!IsActive(query)) return;
        if (await ExpireAsync(query, ct)) return;
        var scope = await ReadScopeAsync(query, ct);
        if (scope is null) return;
        await ExchangeAsync(query, scope, async () =>
        {
            // Retry cancellation rather than reopening SQL authority after a transient failure.
            var response = query.Status == "cancelling"
                ? await client.CancelAsync(scope, ct)
                : await client.GetStatusAsync(scope, ct);
            return PyCaretExecutionContext.Text(response, "status") == "completed"
                ? await client.GetResultAsync(scope, ct) : response;
        }, ct);
    }

    public async Task CancelAsync(QueryHistory query, CancellationToken ct)
    {
        if (!IsActive(query)) return;
        var scope = await ReadScopeAsync(query, ct);
        if (scope is null) return;
        query.Status = "cancelling";
        StoreMessage(query, "Cancellation requested; running SQL may finish but further calls are blocked.");
        if (db.Database.IsRelational())
        {
            // Cancellation must also win over a concurrent nonterminal progress update.
            await db.QueryHistories.Where(row => row.Id == query.Id &&
                (row.Status == "processing" || row.Status == "queued" || row.Status == "cancelling"))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, "cancelling")
                    .SetProperty(row => row.ResultJson, query.ResultJson), ct);
            await db.Entry(query).ReloadAsync(ct);
        }
        else await db.SaveChangesAsync(ct);
        if (query.Status != "cancelling") return;
        await ExchangeAsync(query, scope, () => client.CancelAsync(scope, ct), ct);
    }

    private async Task ExchangeAsync(QueryHistory query, PyCaretExecutionContext scope,
        Func<Task<JsonObject>> operation, CancellationToken ct)
    {
        try
        {
            var response = await operation();
            if (!scope.Matches(response))
                throw new PyCaretClientException("PyCaret response execution scope does not match.", System.Net.HttpStatusCode.Conflict);
            ApplyResponse(query, scope, response);
        }
        catch (PyCaretClientException exception)
        {
            logger.LogWarning("PyCaret exchange failed for query {QueryId}; HTTP {StatusCode}", query.Id, exception.StatusCode);
            if (exception.IsTransient) StoreMessage(query, exception.Message);
            else Fail(query, exception.Message);
        }
        if (!await ExpireAsync(query, ct)) await PersistAsync(query, ct);
    }

    private static void ApplyResponse(QueryHistory query, PyCaretExecutionContext scope, JsonObject response)
    {
        var status = PyCaretExecutionContext.Text(response, "status");
        if (status is not ("queued" or "processing" or "completed" or "failed" or "needs_clarification" or "cancelled" or "cancelling"))
            throw new PyCaretClientException("PyCaret returned an unknown job status.", System.Net.HttpStatusCode.Conflict);

        if (query.Status == "cancelling" && status is "queued" or "processing")
        {
            StoreMessage(query, "Cancellation is pending; further SQL calls are blocked.");
            return;
        }

        // Locally queued jobs stay processing so the first worker callback is authorized.
        query.Status = status == "queued" ? "processing" : status == "needs_clarification" ? "clarification" : status;
        response[PyCaretExecutionContext.PropertyName] = scope.ToJson();
        if (status == "needs_clarification")
        {
            var question = PyCaretExecutionContext.Text(response, "clarificationQuestion")
                ?? PyCaretExecutionContext.Text(response, "error")
                ?? PyCaretExecutionContext.Text(response, "message") ?? "Please confirm the proposed relationships.";
            query.ClarificationQuestion = question[..Math.Min(question.Length, MaxClarificationLength)];
            response["clarificationQuestion"] = query.ClarificationQuestion;
            response["needsClarification"] = true;
            var pending = response["pendingConfirmations"] as JsonArray
                ?? (response["audit"] as JsonObject)?["pendingConfirmations"] as JsonArray ?? new JsonArray();
            response["pendingConfirmations"] = pending.DeepClone();
            var parameters = JsonNode.Parse(query.PyCaretParamsJson)!.AsObject();
            parameters[RelationshipDictionary.ProposalsProperty] = pending.DeepClone();
            query.PyCaretParamsJson = parameters.ToJsonString();
        }
        if (status == "failed")
            response["error"] = PyCaretExecutionContext.Text(response, "error")
                ?? PyCaretExecutionContext.Text(response, "message") ?? "Analysis failed.";
        query.ResultJson = response.ToJsonString();
        if (!IsActive(query)) query.CompletedAt = DateTime.UtcNow;
    }

    private async Task<bool> ExpireAsync(QueryHistory query, CancellationToken ct)
    {
        var minutes = configuration.GetValue<int?>("PyCaret:MaxJobDurationMinutes") ?? DefaultMaxDurationMinutes;
        if (!IsActive(query) || DateTime.UtcNow - query.CreatedAt < TimeSpan.FromMinutes(Math.Max(1, minutes))) return false;
        Fail(query, "Analysis exceeded its maximum duration; submit a new query.");
        await PersistAsync(query, ct);
        return true;
    }

    private async Task<PyCaretExecutionContext?> ReadScopeAsync(QueryHistory query, CancellationToken ct)
    {
        try { return RequireScope(query); }
        catch (JsonException)
        {
            Fail(query, "The original query execution context is missing or invalid; submit a new query.");
            await PersistAsync(query, ct);
            return null;
        }
    }

    private static PyCaretExecutionContext RequireScope(QueryHistory query)
    {
        var scope = PyCaretExecutionContext.Read(query);
        if (scope is null || scope.QueryId != query.Id || scope.ConfigId != query.ConfigId ||
            scope.ConnectionId == Guid.Empty || string.IsNullOrWhiteSpace(scope.CompanyId) ||
            scope.ConfigHash is not { Length: 64 } ||
            scope.ConfigHash.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new JsonException("Missing original execution context.");
        return scope;
    }

    private static bool IsActive(QueryHistory query) => query.Status is "processing" or "queued" or "cancelling";

    private static void StoreMessage(QueryHistory query, string message)
    {
        var stored = JsonNode.Parse(query.ResultJson)!.AsObject();
        stored["message"] = message;
        query.ResultJson = stored.ToJsonString();
    }

    private static void Fail(QueryHistory query, string message)
    {
        query.Status = "failed";
        query.CompletedAt = DateTime.UtcNow;
        JsonObject stored;
        try { stored = JsonNode.Parse(query.ResultJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { stored = new JsonObject(); }
        stored["error"] = message;
        stored["message"] = message;
        query.ResultJson = stored.ToJsonString();
    }

    private async Task PersistAsync(QueryHistory query, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        // Compare-and-swap prevents an in-flight poll from undoing cancellation or a terminal result.
        var entry = db.Entry(query);
        var originalStatus = entry.Property(q => q.Status).OriginalValue;
        var originalResult = entry.Property(q => q.ResultJson).OriginalValue;
        await db.QueryHistories.Where(q => q.Id == query.Id && q.Status == originalStatus && q.ResultJson == originalResult)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(q => q.Status, query.Status)
                .SetProperty(q => q.ResultJson, query.ResultJson)
                .SetProperty(q => q.PyCaretParamsJson, query.PyCaretParamsJson)
                .SetProperty(q => q.ClarificationQuestion, query.ClarificationQuestion)
                .SetProperty(q => q.CompletedAt, query.CompletedAt), ct);
        await entry.ReloadAsync(ct);
    }
}
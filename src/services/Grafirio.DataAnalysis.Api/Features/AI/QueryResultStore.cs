using System.Collections.Concurrent;

namespace Grafirio.DataAnalysis.Api.Features.AI;

/// <summary>
/// In-memory store for AI query results (can be replaced with Redis later)
/// </summary>
public class QueryResultStore
{
    private readonly ConcurrentDictionary<Guid, QueryResult> _results = new();
    private readonly ILogger<QueryResultStore> _logger;

    public QueryResultStore(ILogger<QueryResultStore> logger)
    {
        _logger = logger;
    }

    public void StoreResult(Guid requestId, object result, string status = "completed")
    {
        var queryResult = new QueryResult
        {
            RequestId = requestId,
            Result = result,
            Status = status,
            CompletedAt = DateTime.UtcNow
        };

        _results[requestId] = queryResult;
        _logger.LogInformation("📦 Stored result for RequestId: {RequestId}, Status: {Status}", requestId, status);
    }

    public void UpdateProgress(Guid requestId, int progress, string message)
    {
        if (_results.TryGetValue(requestId, out var existing))
        {
            existing.Progress = progress;
            existing.ProgressMessage = message;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _results[requestId] = new QueryResult
            {
                RequestId = requestId,
                Progress = progress,
                ProgressMessage = message,
                Status = "processing",
                UpdatedAt = DateTime.UtcNow
            };
        }

        _logger.LogInformation("📊 Progress update: {RequestId} - {Progress}% - {Message}", requestId, progress, message);
    }

    public QueryResult? GetResult(Guid requestId)
    {
        _results.TryGetValue(requestId, out var result);
        return result;
    }

    public void CleanOldResults(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var oldKeys = _results
            .Where(kvp => kvp.Value.CompletedAt.HasValue && kvp.Value.CompletedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldKeys)
        {
            _results.TryRemove(key, out _);
        }

        if (oldKeys.Count > 0)
        {
            _logger.LogInformation("🧹 Cleaned {Count} old results", oldKeys.Count);
        }
    }
}

public class QueryResult
{
    public Guid RequestId { get; set; }
    public object? Result { get; set; }
    public string Status { get; set; } = "processing"; // processing, completed, failed
    public int Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

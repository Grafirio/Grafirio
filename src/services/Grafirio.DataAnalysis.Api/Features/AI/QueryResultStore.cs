using StackExchange.Redis;
using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Features.AI;

/// <summary>
/// Redis destekli AI sorgu sonucu deposu.
/// In-memory değil → servis yeniden başlasa veya birden fazla instance çalışsa da veri korunur.
/// </summary>
public class QueryResultStore
{
    private readonly IDatabase _redis;
    private readonly ILogger<QueryResultStore> _logger;
    private static readonly TimeSpan Expiry = TimeSpan.FromHours(2);
    private const string KeyPrefix = "ai:result:";

    public QueryResultStore(IConnectionMultiplexer redis, ILogger<QueryResultStore> logger)
    {
        _redis = redis.GetDatabase();
        _logger = logger;
    }

    public void StoreResult(Guid requestId, object result, string status = "completed")
    {
        var queryResult = new QueryResult
        {
            RequestId    = requestId,
            Result       = result,
            Status       = status,
            CompletedAt  = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow,
        };

        var json = JsonSerializer.Serialize(queryResult);
        _redis.StringSet(KeyPrefix + requestId, json, Expiry);
        _logger.LogInformation("📦 Redis'e yazıldı | RequestId: {RequestId} | Status: {Status}", requestId, status);
    }

    public void UpdateProgress(Guid requestId, int progress, string message)
    {
        var existing = GetResult(requestId);
        var queryResult = existing ?? new QueryResult { RequestId = requestId };

        queryResult.Progress        = progress;
        queryResult.ProgressMessage = message;
        queryResult.Status          = "processing";
        queryResult.UpdatedAt       = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(queryResult);
        _redis.StringSet(KeyPrefix + requestId, json, Expiry);
        _logger.LogInformation("📊 İlerleme güncellendi | {RequestId} — %{Progress} — {Message}", requestId, progress, message);
    }

    public QueryResult? GetResult(Guid requestId)
    {
        var json = _redis.StringGet(KeyPrefix + requestId);
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<QueryResult>((string)json!);
    }
}

public class QueryResult
{
    public Guid    RequestId       { get; set; }
    public object? Result          { get; set; }
    public string  Status          { get; set; } = "processing";
    public int     Progress        { get; set; }
    public string? ProgressMessage { get; set; }
    public DateTime?  CompletedAt  { get; set; }
    public DateTime   UpdatedAt    { get; set; } = DateTime.UtcNow;
}


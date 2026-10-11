using System.Text.Json;
using StackExchange.Redis;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

public sealed class RedisBridgeConnectionDirectory(IConnectionMultiplexer redis) : IBridgeConnectionDirectory
{
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromSeconds(90);
    private const string RefreshScript = "if redis.call('get',KEYS[1]) == ARGV[1] then return redis.call('expire',KEYS[1],ARGV[2]) else return 0 end";
    private const string RemoveScript = "if redis.call('get',KEYS[1]) == ARGV[1] then return redis.call('del',KEYS[1]) else return 0 end";
    private static RedisKey Key(Guid bridgeId) => "grafirio:bridge:presence:v1:" + bridgeId.ToString("N");

    public async Task SetAsync(Guid bridgeId, BridgeConnectionLease lease, CancellationToken ct) =>
        await redis.GetDatabase().StringSetAsync(Key(bridgeId), JsonSerializer.Serialize(lease), LeaseLifetime).WaitAsync(ct);

    public async Task<BridgeConnectionLease?> FindAsync(Guid bridgeId, CancellationToken ct)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(bridgeId)).WaitAsync(ct);
        if (value.IsNullOrEmpty) return null;
        try { return JsonSerializer.Deserialize<BridgeConnectionLease>((string)value!); }
        catch (JsonException) { return null; }
    }

    public async Task<bool> RefreshAsync(Guid bridgeId, BridgeConnectionLease lease, CancellationToken ct)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(RefreshScript, [Key(bridgeId)],
            [JsonSerializer.Serialize(lease), (int)LeaseLifetime.TotalSeconds]).WaitAsync(ct);
        return (int)result == 1;
    }

    public async Task RemoveAsync(Guid bridgeId, BridgeConnectionLease lease, CancellationToken ct) =>
        await redis.GetDatabase().ScriptEvaluateAsync(RemoveScript, [Key(bridgeId)],
            [JsonSerializer.Serialize(lease)]).WaitAsync(ct);
}

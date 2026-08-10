using System.Text.Json;
using StackExchange.Redis;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Cok replikali kurulumun cevap yolu.
///
/// SignalR'in Redis backplane'i istegi bridge'e ulastiriyor ama cevap,
/// bridge'in bagli oldugu replikaya duser. Buradaki is o cevabi sorguyu
/// BASLATAN replikaya geri yollamak.
///
/// Nasil calisiyor:
///   1. Sorguyu baslatan replika, Redis'e <c>requestId → instanceId</c> yaziyor.
///   2. Cevap hangi replikaya duserse dussun, o sahibi okuyor.
///   3. Sahip kendisiyse dogrudan teslim; degilse sahibin kanalina yayinliyor.
///
/// Protokol degismiyor: bridge hangi replikanin bekledigini bilmiyor.
///
/// Sahiplik kaydinda TTL var: bir replika sorgu ortasinda olurse kayit
/// sonsuza kadar kalmasin. Sure sorgu zaman asimindan uzun secildi, yoksa
/// hala calisan bir sorgunun sahipligi dusebilirdi.
/// </summary>
public sealed class RedisBridgeResponseBus : IBridgeResponseBus, IAsyncDisposable
{
    private const string OwnerKeyPrefix = "grafirio:bridge:owner:";
    private const string ChannelPrefix = "grafirio:bridge:responses:";

    private static readonly TimeSpan OwnershipLifetime = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBridgeResponseBus> _logger;
    private Func<BridgeResponse, CancellationToken, Task>? _handler;

    public string InstanceId { get; } =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"[..48];

    public RedisBridgeResponseBus(
        IConnectionMultiplexer redis, ILogger<RedisBridgeResponseBus> logger)
    {
        _redis = redis;
        _logger = logger;

        // Kendi kanalimizi acilista dinlemeye basliyoruz — istek basina abone
        // olmak, on binlerce sorguda Redis'te on binlerce abonelik demekti.
        _redis.GetSubscriber().Subscribe(
            RedisChannel.Literal(ChannelPrefix + InstanceId), OnMessage);

        logger.LogInformation("Bridge cevap kanalı Redis üzerinden. Örnek: {InstanceId}", InstanceId);
    }

    public Task ClaimAsync(string requestId, CancellationToken ct = default) =>
        _redis.GetDatabase().StringSetAsync(
            OwnerKeyPrefix + requestId, InstanceId, OwnershipLifetime);

    public Task ReleaseAsync(string requestId, CancellationToken ct = default) =>
        _redis.GetDatabase().KeyDeleteAsync(OwnerKeyPrefix + requestId);

    public async Task DispatchAsync(BridgeResponse response, CancellationToken ct = default)
    {
        var owner = await _redis.GetDatabase().StringGetAsync(OwnerKeyPrefix + response.RequestId);

        // Sahip yok: iptal edilmis ya da zaman asimina ugramis bir sorgunun
        // gec gelen parcasi. Normal durum, sessizce dusuyor.
        if (owner.IsNullOrEmpty) return;

        if (owner == InstanceId)
        {
            if (_handler is { } handler) await handler(response, ct);
            return;
        }

        await _redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(ChannelPrefix + owner),
            JsonSerializer.Serialize(response, JsonOptions));
    }

    public void OnLocalDelivery(Func<BridgeResponse, CancellationToken, Task> handler) =>
        _handler = handler;

    private void OnMessage(RedisChannel channel, RedisValue value)
    {
        if (_handler is not { } handler) return;

        try
        {
            // Acikca string'e cevriliyor: RedisValue hem string'e hem
            // ReadOnlySpan<byte>'a ortuk donusuyor ve asiri yukleme secimi
            // belirsiz kaliyor.
            var response = JsonSerializer.Deserialize<BridgeResponse>(
                (string)value!, JsonOptions);
            if (response is null) return;

            // Redis'in abonelik geri cagrisi senkron; beklemek onu bloklardi.
            // Teslim ayri bir gorevde yapiliyor, hatasi da yutulmuyor.
            _ = Task.Run(async () =>
            {
                try
                {
                    await handler(response, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Yönlendirilen cevap teslim edilemedi. İstek: {RequestId}",
                        response.RequestId);
                }
            });
        }
        catch (JsonException ex)
        {
            // Bozuk mesaj tum kanali dusurmemeli.
            _logger.LogError(ex, "Bridge cevabı çözülemedi.");
        }
    }

    public async ValueTask DisposeAsync() =>
        await _redis.GetSubscriber().UnsubscribeAsync(
            RedisChannel.Literal(ChannelPrefix + InstanceId));
}

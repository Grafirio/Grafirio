using System.Collections.Concurrent;
using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Cevabın, sorguyu BAŞLATAN replikaya ulaşması.
///
/// Bridge tek bir replikaya bağlı. Sorgu isteği başka bir replikada doğduysa
/// SignalR'ın Redis backplane'i isteği bridge'e ulaştırır — ama cevap
/// bridge'in bağlı olduğu replikaya düşer ve orada bekleyen kimse yoktur.
/// Backplane yalnızca sunucudan istemciye gideni taşır.
///
/// Burada iki replika taklit ediliyor: <c>A</c> sorguyu başlatıyor, cevap
/// <c>B</c>'ye düşüyor ve A'ya yönlendirilmesi gerekiyor. Bu yönlendirme
/// çalışmazsa sorgu sessizce zaman aşımına uğrar — teşhis edilmesi en zor
/// hata türü.
/// </summary>
public class BridgeResponseRoutingTests
{
    private static readonly Guid BridgeId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// Redis'in yerine geçen paylaşımlı otobüs: sahiplik kaydı ve örnekler
    /// arası teslim. Gerçeğinde bunlar Redis anahtarı ve pub/sub kanalı.
    /// </summary>
    private sealed class SharedBus
    {
        public readonly ConcurrentDictionary<string, string> Owners = new();

        public readonly ConcurrentDictionary<
            string, Func<BridgeResponse, CancellationToken, Task>> Instances = new();

        public int CrossInstanceDeliveries;
    }

    private sealed class FakeBus(SharedBus shared, string instanceId) : IBridgeResponseBus
    {
        public string InstanceId => instanceId;

        public Task ClaimAsync(string requestId, CancellationToken ct = default)
        {
            shared.Owners[requestId] = instanceId;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string requestId, CancellationToken ct = default)
        {
            shared.Owners.TryRemove(requestId, out _);
            return Task.CompletedTask;
        }

        public async Task DispatchAsync(BridgeResponse response, CancellationToken ct = default)
        {
            if (!shared.Owners.TryGetValue(response.RequestId, out var owner)) return;

            if (owner != instanceId) Interlocked.Increment(ref shared.CrossInstanceDeliveries);

            if (shared.Instances.TryGetValue(owner, out var handler))
                await handler(response, ct);
        }

        public void OnLocalDelivery(Func<BridgeResponse, CancellationToken, Task> handler) =>
            shared.Instances[instanceId] = handler;
    }

    private static BridgeRegistry Replica(SharedBus shared, string instanceId)
    {
        var registry = new BridgeRegistry(
            new FakeBus(shared, instanceId), NullLogger<BridgeRegistry>.Instance);

        registry.Start();
        return registry;
    }

    private static QueryChunk Chunk(string requestId, string value) =>
        new(requestId, 0,
            [new QueryColumn("Ad", SqlValueKind.Text)],
            [[value]]);

    [Fact]
    public async Task Baska_replikaya_dusen_cevap_sahibine_yonlendiriliyor()
    {
        var shared = new SharedBus();
        var replicaA = Replica(shared, "A");
        var replicaB = Replica(shared, "B");

        // Sorgu A'da doğdu.
        var pending = await replicaA.RegisterAsync("istek-1", BridgeId);

        // Cevap B'ye düştü: bridge B'ye bağlı.
        await replicaB.DispatchAsync(new BridgeResponse("istek-1", Chunk: Chunk("istek-1", "Ada")));
        await replicaB.DispatchAsync(
            new BridgeResponse("istek-1", Completed: new QueryCompleted("istek-1", 1, false, [])));

        var rows = new List<QueryChunk>();
        await foreach (var chunk in pending.Reader.ReadAllAsync()) rows.Add(chunk);

        Assert.Single(rows);
        Assert.Equal("Ada", rows[0].Rows[0][0]);

        // İkisi de yönlendirildi: satır parçası ve "tamamlandı". İkincisi
        // ulaşmazsa okuma tarafı akışın bittiğini hiç öğrenemez.
        Assert.Equal(2, shared.CrossInstanceDeliveries);
    }

    [Fact]
    public async Task Ayni_replikadaki_cevap_yonlendirilmeden_teslim_ediliyor()
    {
        var shared = new SharedBus();
        var replicaA = Replica(shared, "A");

        var pending = await replicaA.RegisterAsync("istek-2", BridgeId);

        await replicaA.DispatchAsync(new BridgeResponse("istek-2", Chunk: Chunk("istek-2", "Bora")));
        await replicaA.DispatchAsync(
            new BridgeResponse("istek-2", Completed: new QueryCompleted("istek-2", 1, false, [])));

        var rows = new List<QueryChunk>();
        await foreach (var chunk in pending.Reader.ReadAllAsync()) rows.Add(chunk);

        Assert.Single(rows);
        // Tek replikada Redis turu yapılmamalı: gereksiz gecikme olurdu.
        Assert.Equal(0, shared.CrossInstanceDeliveries);
    }

    [Fact]
    public async Task Hata_da_sahibine_yonlendiriliyor()
    {
        var shared = new SharedBus();
        var replicaA = Replica(shared, "A");
        var replicaB = Replica(shared, "B");

        var pending = await replicaA.RegisterAsync("istek-3", BridgeId);

        await replicaB.DispatchAsync(new BridgeResponse("istek-3",
            Failure: new QueryFailure("istek-3", QueryFailure.NotReadOnly, "okuma değil")));

        var ex = await Assert.ThrowsAsync<BridgeQueryException>(async () =>
        {
            await foreach (var _ in pending.Reader.ReadAllAsync()) { }
        });

        Assert.Equal(QueryFailure.NotReadOnly, ex.Code);
    }

    [Fact]
    public async Task Sahibi_kalmamis_cevap_sessizce_dusuyor()
    {
        // İptal edilmiş ya da zaman aşımına uğramış bir sorgunun geç gelen
        // parçası normal bir durum; hata vermemeli.
        var shared = new SharedBus();
        var replicaA = Replica(shared, "A");
        var replicaB = Replica(shared, "B");

        await replicaA.RegisterAsync("istek-4", BridgeId);
        await replicaA.ReleaseAsync("istek-4");

        await replicaB.DispatchAsync(new BridgeResponse("istek-4", Chunk: Chunk("istek-4", "geç")));

        Assert.Equal(0, shared.CrossInstanceDeliveries);
    }

    [Fact]
    public async Task Sorgu_bitince_sahiplik_birakiliyor()
    {
        // Bırakılmazsa Redis'te her sorgudan bir anahtar kalırdı.
        var shared = new SharedBus();
        var replicaA = Replica(shared, "A");

        await replicaA.RegisterAsync("istek-5", BridgeId);
        Assert.True(shared.Owners.ContainsKey("istek-5"));

        await replicaA.ReleaseAsync("istek-5");
        Assert.False(shared.Owners.ContainsKey("istek-5"));
    }

    [Fact]
    public async Task Bridge_koptugunda_bekleyen_sorgu_hata_aliyor()
    {
        // Bridge'in bağlı olduğu replika onu düşürdüğünde, başka bir
        // replikada bekleyen sorgu sonsuza kadar beklememeli.
        var shared = new SharedBus();
        var replicaA = Replica(shared, "A");

        var pending = await replicaA.RegisterAsync("istek-6", BridgeId);

        replicaA.Attach(BridgeId, "conn-1", "firma-a", "1");
        replicaA.Detach(BridgeId, "conn-1");

        await Assert.ThrowsAsync<BridgeUnavailableException>(async () =>
        {
            await foreach (var _ in pending.Reader.ReadAllAsync()) { }
        });
    }
}

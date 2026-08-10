using System.Collections.Concurrent;
using System.Threading.Channels;
using Grafirio.Bridge.Contracts;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Bagli bridge'ler ve suren sorgular.
///
/// Iki ayri sorumluluk var ve ikisi farkli yerlerde yasiyor:
///
///   * <b>Hangi bridge nereye bagli</b> — sureclere ozel. Bridge tek bir
///     replikaya baglaniyor. Sorgu istegi baska bir replikada dogduysa
///     SignalR'in Redis backplane'i istegi yine de ulastirir.
///   * <b>Cevabi kim bekliyor</b> — <see cref="IBridgeResponseBus"/>. Backplane
///     yalnizca sunucudan istemciye gideni tasidigi icin cevap yolu ayrica
///     kuruluyor.
///
/// Bridge'e ulasilamadiginda acik bir hata firlatiliyor
/// (<see cref="BridgeUnavailableException"/>), sessizce bekleyen bir istek
/// degil — teshis edilemeyen bir askida kalma, gorulebilen bir hatadan cok
/// daha kotudur.
/// </summary>
public class BridgeRegistry(IBridgeResponseBus responses, ILogger<BridgeRegistry> logger)
{
    /// <summary>Bridge kimligi → SignalR baglanti kimligi.</summary>
    private readonly ConcurrentDictionary<Guid, BridgeConnection> _connected = new();

    /// <summary>Suren sorgular: istek kimligi → cevabin akitilacagi kanal.</summary>
    private readonly ConcurrentDictionary<string, PendingQuery> _pending = new();

    public void Attach(Guid bridgeId, string connectionId, string companyId, string version)
    {
        // Ayni bridge yeniden baglandiysa eskisinin yerine gecer. Eski kaydi
        // birakmak, sorgularin olu bir baglantiya gitmesi demek.
        _connected[bridgeId] = new BridgeConnection(connectionId, companyId, version);

        logger.LogInformation(
            "Bridge bağlandı. Id: {BridgeId}, sürüm: {Version}", bridgeId, version);
    }

    public void Detach(Guid bridgeId, string connectionId)
    {
        // Kosul onemli: bridge yeniden baglandiktan SONRA eski baglantinin
        // kapanma bildirimi gelebiliyor. Kosulsuz silmek, yeni ve saglikli
        // baglantiyi defterden dusururdu.
        if (_connected.TryGetValue(bridgeId, out var existing)
            && existing.ConnectionId == connectionId)
        {
            _connected.TryRemove(bridgeId, out _);
        }

        // Bu bridge'e gonderilmis suren sorgular sonsuza kadar beklemesin.
        foreach (var (requestId, pending) in _pending)
        {
            if (pending.BridgeId != bridgeId) continue;

            pending.Fail(new BridgeUnavailableException(
                "Bridge bağlantısı koptu; sorgu tamamlanamadı."));
            _pending.TryRemove(requestId, out _);
        }

        logger.LogInformation("Bridge ayrıldı. Id: {BridgeId}", bridgeId);
    }

    public bool IsOnline(Guid bridgeId) => _connected.ContainsKey(bridgeId);

    public IReadOnlyCollection<Guid> OnlineBridgeIds => _connected.Keys.ToList();

    /// <summary>
    /// Bridge'in SignalR baglanti kimligi. Bagli degilse — ya da baska bir
    /// replikaya bagliysa — hata.
    /// </summary>
    public string ConnectionIdOf(Guid bridgeId, string companyId)
    {
        if (!_connected.TryGetValue(bridgeId, out var connection))
            throw new BridgeUnavailableException(
                "Bridge şu anda çevrimdışı. Müşteri sunucusundaki Grafirio Bridge " +
                "servisinin çalıştığını kontrol edin.");

        // Sirket suzgeci: baska bir sirketin bridge kimligini bilen biri onun
        // uzerinden sorgu calistiramasin.
        if (connection.CompanyId != companyId)
            throw new BridgeUnavailableException("Bridge bu şirkete ait değil.");

        return connection.ConnectionId;
    }

    /* ── Suren sorgular ───────────────────────────────────────────────── */

    /// <summary>
    /// Bu ornege yonlendirilen cevaplari bagla. Program acilista bir kez cagirir.
    /// </summary>
    public void Start() => responses.OnLocalDelivery(Deliver);

    public async Task<PendingQuery> RegisterAsync(
        string requestId, Guid bridgeId, CancellationToken ct = default)
    {
        var pending = new PendingQuery(bridgeId);
        _pending[requestId] = pending;

        // Sahiplik kaydi sorgu GONDERILMEDEN once yaziliyor: cevap, istek
        // gonderildikten hemen sonra baska bir replikaya dusebilir.
        await responses.ClaimAsync(requestId, ct);
        return pending;
    }

    public async Task ReleaseAsync(string requestId)
    {
        _pending.TryRemove(requestId, out _);
        await responses.ReleaseAsync(requestId);
    }

    /// <summary>
    /// Bridge'ten gelen cevabi sahibine yollar.
    ///
    /// Hub bunu cagiriyor ve cagiran replika sahibi OLMAYABILIR: bridge hangi
    /// replikaya bagliysa cevap oraya duser. Yonlendirmeyi otobüs yapiyor.
    /// </summary>
    public Task DispatchAsync(BridgeResponse response, CancellationToken ct = default) =>
        responses.DispatchAsync(response, ct);

    /// <summary>
    /// Cevabi bekleyen kanala yazar.
    ///
    /// Bilinmeyen istek kimligi sessizce atiliyor: iptal edilmis ya da zaman
    /// asimina ugramis bir sorgunun gec gelen parcasi normal bir durum.
    /// </summary>
    private async Task Deliver(BridgeResponse response, CancellationToken ct)
    {
        if (!_pending.TryGetValue(response.RequestId, out var pending)) return;

        if (response.Failure is { } failure)
        {
            pending.Fail(new BridgeQueryException(failure.Code, failure.Message));
            return;
        }

        if (response.Completed is { } completed)
        {
            pending.Complete(completed);
            return;
        }

        // Kanal sinirli; dolu oldugunda yazma bekler. Beklemek burada DOGRU
        // davranis: bridge'in sunucudan hizli satir uretmesi boyle freniyor.
        if (response.Chunk is { } chunk) await pending.WriteAsync(chunk, ct);
    }

    private sealed record BridgeConnection(string ConnectionId, string CompanyId, string Version);
}

/// <summary>
/// Bekleyen bir sorgunun cevap kanali.
///
/// Kanal sinirli (<see cref="Capacity"/>): bridge sunucudan hizli satir
/// uretirse yazma tarafi bekler. Sinirsiz olsaydi yavas tuketilen buyuk bir
/// sonuc sunucunun bellegini doldururdu.
/// </summary>
public sealed class PendingQuery(Guid bridgeId)
{
    private const int Capacity = 8;

    private readonly Channel<QueryChunk> _channel =
        Channel.CreateBounded<QueryChunk>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public Guid BridgeId { get; } = bridgeId;

    public QueryCompleted? Result { get; private set; }

    public ChannelReader<QueryChunk> Reader => _channel.Reader;

    public ValueTask WriteAsync(QueryChunk chunk, CancellationToken ct) =>
        _channel.Writer.WriteAsync(chunk, ct);

    public void Complete(QueryCompleted completed)
    {
        Result = completed;
        _channel.Writer.TryComplete();
    }

    public void Fail(Exception exception) => _channel.Writer.TryComplete(exception);
}

/// <summary>Bridge'e ulasilamiyor: cevrimdisi ya da baska bir replikada.</summary>
public class BridgeUnavailableException(string message) : Exception(message);

/// <summary>Bridge sorguyu calistirdi ve hata dondu.</summary>
public class BridgeQueryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

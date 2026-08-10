using System.Collections.Concurrent;
using System.Threading.Channels;
using Grafirio.Bridge.Contracts;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Bu sunucu ornegine bagli bridge'ler ve suren sorgular.
///
/// <b>Bu defter sureclere ozel.</b> Bridge tek bir replikaya baglaniyor; sorgu
/// istegi baska bir replikaya duserse o replika bridge'i bulamaz. Bugun bunun
/// karsiligi acik bir hata (<see cref="BridgeUnavailableException"/>), sessizce
/// bekleyen bir istek degil — teshis edilemeyen bir askida kalma, gorulebilen
/// bir hatadan cok daha kotudur.
///
/// Cok replikali calisma icin cevap kanalinin da replikalar arasi tasinmasi
/// gerekiyor (Redis pub/sub). SignalR'in Redis backplane'i tek basina yetmez:
/// o yalnizca sunucudan istemciye gideni tasir, bridge'in cevabi yine
/// baglandigi replikaya duser. O gelene kadar data-analysis-api tek replika
/// calismali.
/// </summary>
public class BridgeRegistry(ILogger<BridgeRegistry> logger)
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

    public PendingQuery Register(string requestId, Guid bridgeId)
    {
        var pending = new PendingQuery(bridgeId);
        _pending[requestId] = pending;
        return pending;
    }

    public void Release(string requestId) => _pending.TryRemove(requestId, out _);

    /// <summary>
    /// Bridge'ten gelen parcayi bekleyen tarafa aktarir.
    ///
    /// Bilinmeyen istek kimligi sessizce atiliyor: iptal edilmis ya da zaman
    /// asimina ugramis bir sorgunun gec gelen parcasi normal bir durum.
    /// </summary>
    public async Task PushAsync(string requestId, QueryChunk chunk, CancellationToken ct)
    {
        if (_pending.TryGetValue(requestId, out var pending))
            await pending.WriteAsync(chunk, ct);
    }

    public void Complete(string requestId, QueryCompleted completed)
    {
        if (_pending.TryGetValue(requestId, out var pending))
            pending.Complete(completed);
    }

    public void Fail(string requestId, QueryFailure failure)
    {
        if (_pending.TryGetValue(requestId, out var pending))
            pending.Fail(new BridgeQueryException(failure.Code, failure.Message));
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

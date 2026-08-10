using Grafirio.Bridge.Contracts;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Bridge'in cevabini, sorguyu BASLATAN sunucu ornegine ulastirir.
///
/// Neden gerekiyor: bridge tek bir replikaya bagli. Sorgu istegi baska bir
/// replikada dogduysa, SignalR'in Redis backplane'i istegi bridge'e ulastirir
/// — ama cevap bridge'in bagli oldugu replikaya duser ve orada bekleyen kimse
/// yoktur. Backplane yalnizca sunucudan istemciye gideni tasir.
///
/// Bu arayuz eksik yonu kapatiyor. Protokol degismiyor: <b>bridge hangi
/// replikanin bekledigini bilmiyor</b>. Sahiplik sunucu tarafinda tutuluyor,
/// cunku bunu bridge'e soylemek, musteri makinesindeki bir yazilimi bizim
/// olceklendirme kararlarimiza bagimli kilardi.
/// </summary>
public interface IBridgeResponseBus
{
    /// <summary>Bu sunucu orneginin kimligi. Sahiplik kaydinda bu yaziyor.</summary>
    string InstanceId { get; }

    /// <summary>
    /// Sorgunun sahibini kaydeder. Cevap baska bir replikaya dustugunde
    /// oradan buraya yonlendirilebilmesinin tek yolu bu kayit.
    /// </summary>
    Task ClaimAsync(string requestId, CancellationToken ct = default);

    /// <summary>Sorgu bittiginde sahiplik kaydini birakir.</summary>
    Task ReleaseAsync(string requestId, CancellationToken ct = default);

    /// <summary>
    /// Cevabi sahibine yollar. Sahip bu ornekse dogrudan teslim edilir.
    ///
    /// Sahip bulunamazsa mesaj sessizce dusuyor — iptal edilmis ya da zaman
    /// asimina ugramis bir sorgunun gec gelen parcasi normal bir durum.
    /// </summary>
    Task DispatchAsync(BridgeResponse response, CancellationToken ct = default);

    /// <summary>
    /// Bu ornege yonlendirilen cevaplari teslim eder.
    /// <see cref="BridgeRegistry"/> acilista bagliyor.
    ///
    /// Asenkron cunku teslim, sinirli bir kanala yazmak demek: kanal doluysa
    /// beklemek DOGRU davranis, bridge'in sunucudan hizli satir uretmesi boyle
    /// frenleniyor. Senkron bir geri cagri burada bir is parcacigini bloklardi.
    /// </summary>
    void OnLocalDelivery(Func<BridgeResponse, CancellationToken, Task> handler);
}

/// <summary>
/// Bridge'ten gelen tek bir cevap. Uc bicimden biri dolu olur.
///
/// Tek bir tip cunku ucu de ayni yoldan, ayni sirayla gitmeli: parca, sonra
/// tamamlandi. Ayri kanallar kullanilsaydi sira garantisi kaybolurdu.
/// </summary>
public sealed record BridgeResponse(
    string RequestId,
    QueryChunk? Chunk = null,
    QueryCompleted? Completed = null,
    QueryFailure? Failure = null);

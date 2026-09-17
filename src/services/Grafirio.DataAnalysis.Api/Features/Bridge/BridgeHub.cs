using Grafirio.Bridge.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Musteri agindaki bridge'lerin bagli durdugu kanal.
///
/// Yon: baglantiyi <b>bridge kurar</b>. Sunucu hicbir zaman musteri agina
/// baglanmaya calismaz; acik duran bu kanaldan sorgu gonderir. Musterinin
/// firewall'inda hicbir giris portu acilmaz — satista soylenen cumlenin
/// koddaki karsiligi burasi.
/// </summary>
/// <remarks>
/// Kimlik dogrulama Keycloak'in verdigi JWT ile — bridge'e ozel elle yazilmis
/// bir sema YOK. Bridge kendi Keycloak client'i olarak <c>client_credentials</c>
/// ile token aliyor; token'daki <c>bridge_id</c> ve <c>company_id</c> claim'leri
/// kimligi tasiyor. Iptal, token suresi ve anahtar rotasyonu Keycloak'in isi.
///
/// <paramref name="connectionSync"/> varsayilan degerli: protokolu Mongo
/// olmadan test edebilmek icin. Uretimde kayitli olmadigi bir durum yok —
/// olsaydi bridge baglanir ama hicbir sorgu calistiramazdi, o yuzden
/// eksikligi uyari olarak yaziliyor.
/// </remarks>
[Authorize(Policy = BridgeAuthentication.Policy)]
public class BridgeHub(
    BridgeRegistry registry,
    IBridgePresence presence,
    ILogger<BridgeHub> logger,
    BridgeConnectionSync? connectionSync = null) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var (bridgeId, companyId) = Identify();

        // Protokol surumu baslikta geliyor. Bridge'ler musteri sunucularinda
        // yasiyor ve kendiliginden guncellenmiyor; uyumsuz surumu sessizce
        // kabul etmek, anlasilmaz hatalar uretir.
        var version = Context.GetHttpContext()?.Request.Headers[BridgeAuthentication.VersionHeader]
            .ToString() ?? "";

        if (!version.StartsWith(BridgeProtocol.Version + ".", StringComparison.Ordinal)
            && version != BridgeProtocol.Version)
        {
            logger.LogWarning(
                "Uyumsuz bridge sürümü reddedildi. Id: {BridgeId}, sürüm: {Version}",
                bridgeId, version);

            Context.Abort();
            return;
        }

        registry.Attach(bridgeId, Context.ConnectionId, companyId, version);
        await presence.TouchAsync(bridgeId, version, Context.ConnectionAborted);

        // Baglanti tanimlarini gonder. Bunsuz bridge baglanir, kalp atisi
        // gonderir ve her sorguyu "bu baglanti tanimli degil" diye reddeder.
        //
        // Her baglanista tekrarlaniyor: baglama aninda bridge cevrimdisi
        // olabilir, ayrica sifre ya da tablo secimi sonradan degismis olabilir.
        if (connectionSync is null)
        {
            logger.LogWarning(
                "BridgeConnectionSync kayıtlı değil; bridge {BridgeId} bağlandı ama " +
                "hiçbir bağlantı tanımı gönderilmeyecek.", bridgeId);
        }
        else
        {
            try
            {
                await connectionSync.SyncAllAsync(
                    bridgeId, companyId, Context.ConnectionId, Context.ConnectionAborted);
            }
            catch (Exception ex)
            {
                // Gonderim basarisiz olsa bile baglanti ayakta kalsin:
                // cevrimdisi gorunen bir bridge, tanimsiz baglantidan daha
                // yaniltici olurdu.
                logger.LogError(ex,
                    "Bağlantı tanımları gönderilemedi. Bridge: {BridgeId}", bridgeId);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var (bridgeId, _) = Identify();
        registry.Detach(bridgeId, Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    // Asagidaki uc metot cevabi dogrudan teslim etmiyor, kayit defterine
    // veriyor: bridge'in bagli oldugu replika ile sorguyu baslatan replika
    // ayni olmayabilir. Yonlendirme <see cref="IBridgeResponseBus"/> isi.

    /// <summary>Bridge → sunucu: satir parcasi.</summary>
    public async Task PushChunk(QueryChunk chunk) =>
        await registry.DispatchAsync(
            new BridgeResponse(chunk.RequestId, Chunk: chunk), Context.ConnectionAborted);

    /// <summary>Bridge → sunucu: sorgu bitti.</summary>
    public async Task CompleteQuery(QueryCompleted completed) =>
        await registry.DispatchAsync(
            new BridgeResponse(completed.RequestId, Completed: completed),
            Context.ConnectionAborted);

    /// <summary>Bridge → sunucu: sorgu calistirilamadi.</summary>
    public async Task FailQuery(QueryFailure failure)
    {
        logger.LogWarning(
            "Bridge sorguyu reddetti. Kod: {Code}, mesaj: {Message}",
            failure.Code, failure.Message);

        await registry.DispatchAsync(
            new BridgeResponse(failure.RequestId, Failure: failure), Context.ConnectionAborted);
    }

    /// <summary>Bridge → sunucu: hayattayim. Paneldeki rozeti besleyen sey.</summary>
    public async Task Heartbeat(BridgeHeartbeat heartbeat)
    {
        var (bridgeId, _) = Identify();
        await presence.TouchAsync(bridgeId, heartbeat.BridgeVersion, Context.ConnectionAborted);
    }

    private (Guid BridgeId, string CompanyId) Identify()
    {
        var bridgeId = Context.User?.FindFirst(BridgeAuthentication.BridgeIdClaim)?.Value;
        var companyId = Context.User?.FindFirst(BridgeAuthentication.CompanyIdClaim)?.Value;

        // Policy her ikisini de sart kosuyor; buraya duserse Keycloak'taki
        // claim mapper'lari ile buradaki adlar ayrismis demektir. Sessizce
        // devam etmek yanlis bridge'e sorgu gondermeye yol acar.
        if (bridgeId is null || companyId is null)
            throw new HubException(
                "Bridge kimliği çözülemedi: token'da bridge_id/company_id yok.");

        return (Guid.Parse(bridgeId), companyId);
    }
}

/// <summary>
/// "Bu bridge hayatta." Hub'in <see cref="Data.Mongo.BridgeStore"/>'dan
/// kullandigi tek sey bu; arayuz olarak ayrilmasinin sebebi, protokolun
/// Mongo ayakta olmadan test edilebilmesi.
/// </summary>
public interface IBridgePresence
{
    Task TouchAsync(Guid bridgeId, string version, CancellationToken ct = default);
}

/// <summary>
/// Bridge kimliginin token'daki karsiligi.
///
/// Claim adlari Keycloak tarafindaki hardcoded claim mapper'lariyla ayni
/// olmali (<see cref="KeycloakBridgeIdentity"/>); ikisi ayrisirsa bridge
/// baglanir ama kimligi cozulemez.
/// </summary>
public static class BridgeAuthentication
{
    public const string Policy = "BridgeAccess";
    public const string BridgeIdClaim = "bridge_id";
    public const string CompanyIdClaim = "company_id";
    public const string VersionHeader = "X-Grafirio-Bridge-Version";

    /// <summary>
    /// Bridge politikasi — kimlik dogrulama semasi ACIKCA yazili.
    ///
    /// Paylasilan kurulum (<c>AddAuthenticationAndAuthorizationExt</c>)
    /// <c>AddAuthentication()</c>'i varsayilan sema VERMEDEN cagiriyor ve iki
    /// sema kaydediyor. Varsayilan olmayinca <c>UseAuthentication()</c> hicbir
    /// istegi dogrulamiyor; <c>HttpContext.User</c> anonim kaliyor. Paketin
    /// kendi politikalari (Password, CompanyAccess) semayi kendileri yazdigi
    /// icin calisiyor — burada yazilmayinca yetkilendirme anonim User'a bakip
    /// <c>RequireAuthenticatedUser()</c>'da dusuyordu.
    ///
    /// Gorunen sonuc: token kusursuz olsa bile her bridge negotiate'te 401
    /// aliyordu. Sebebi hicbir yerde yazmiyordu, cunku dogrulama hic
    /// calismadigi icin challenge basligi da bos "Bearer" donuyordu — bozuk
    /// bir token <c>error="invalid_token"</c> derken gecerli olan hic token
    /// yokmus gibi gorunuyordu.
    ///
    /// Uretim ve test ayni yerden okusun diye burada: testin politikayi kendi
    /// kurmasi, tam da bu hatayi gorunmez yapmisti.
    /// </summary>
    public static IServiceCollection AddBridgeAuthorization(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(BridgeIdClaim)
                .RequireClaim(CompanyIdClaim))
            .Services;
}

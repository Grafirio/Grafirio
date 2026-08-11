using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Bridge kurulumu ve yonetimi.
///
/// Kayit akisi:
///   1. Bridge acilista ekranda kisa bir kod gosterir (device flow).
///   2. Kuran kisi tarayicida kendi hesabiyla onaylar.
///   3. Bridge onun token'iyla <c>/enroll</c>'a gelir.
///   4. Sunucu bridge'e KENDI Keycloak kimligini acar ve doner.
///
/// Kurulum dosyasinin kendisi herkese acik durabilir; bir bridge'i sirkete
/// baglayan sey kuran kisinin onayi. Onceki surumde bunun yerine panelden
/// uretilen tek kullanimlik bir token vardi — kendi uretimimiz, kendi
/// ozetimiz, kendi son kullanma mantigimiz — ve device flow'un elle yapilmis
/// halinden ibaretti.
///
/// 3. adimda gelen token kuran KISININ; bridge'in kimligi 4. adimda dogar ve
/// o kisiden bagimsizdir. Kuran kisi sirketten ayrildiginda bridge calismaya
/// devam eder.
/// </summary>
public static class BridgeEndpoints
{
    public static void MapBridgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bridges")
            .WithTags("Bridge")
            .WithOpenApi();

        var managed = group.MapGroup("").RequireAuthorization("CompanyAccess");

        // Kayit ucu artik kimlik dogrulamasi ISTIYOR. Bridge'in henuz kimligi
        // yok ama kuran kisinin var; sirket bilgisi de istekten degil onun
        // token'indan okunuyor.
        managed.MapPost("/enroll", Enroll)
            .WithDescription("Bridge'i, kuran kişinin onayıyla sisteme tanıtır");

        managed.MapGet("/", ListBridges)
            .WithDescription("Şirketin bridge'lerini ve çevrimiçi durumlarını listeler");

        // Kurulum dosyasi ANONIM: managed grubunun disinda. Sirket suzgeci
        // hicbir zaman yoktu (dosya herkes icin ayni ve gizli degil, bir
        // bridge'i sirkete baglayan sey dosya degil giristeki onay) ama oturum
        // sart kosmak, gercek koruma saglamadan panelde bir indirme
        // dugmesinin duz bir baglanti olamamasina yol aciyordu — dosya zaten
        // BridgeInstaller__Url'de public blob'a yonlendiriliyor, isteyen
        // adresi dogrudan da acabilir. Kimlik dogrulamasi burada yalnizca
        // "panele giren biri" olmayi zorlaştiran, kazandirmayan bir engeldi.
        group.MapGet("/installer", (BridgeInstaller installer) => installer.Serve())
            .WithDescription("Grafirio Bridge kurulum dosyasını indirir");

        group.MapGet("/installer/info", (BridgeInstaller installer) =>
                Results.Ok(installer.Describe()))
            .WithDescription("Kurulum dosyasının yayınlanıp yayınlanmadığını bildirir");

        managed.MapDelete("/{bridgeId:guid}", RevokeBridge)
            .WithDescription("Bridge'in erişimini iptal eder");

        managed.MapPut("/connections/{connectionId:guid}", BindConnection)
            .WithDescription("Bağlantının hangi bridge üzerinden okunacağını belirler");

        managed.MapGet("/connections", ListBindings)
            .WithDescription("Hangi bağlantının hangi bridge'e bağlı olduğunu listeler");
    }

    /// <summary>
    /// Cagiranin sirketi. Istekten okunmuyor, token'dan geliyor: aksi halde
    /// baska bir sirkete bridge tanitmak mumkun olurdu.
    /// </summary>
    private static string? CompanyOf(IIdentityService identity) =>
        identity.CurrentCompanyId?.ToString();

    private static async Task<IResult> Enroll(
        EnrollRequest request,
        BridgeStore store,
        KeycloakBridgeIdentity identity,
        IIdentityService callerIdentity,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("BridgeEnrollment");

        if (request.ProtocolVersion != BridgeProtocol.Version)
            return Results.BadRequest(new
            {
                error = $"Bu bridge sürümü sunucuyla uyumlu değil " +
                        $"(bridge: {request.ProtocolVersion}, sunucu: {BridgeProtocol.Version}). " +
                        "Lütfen güncel kurulum dosyasını indirin."
            });

        // Sirket istekten DEGIL, kuran kisinin token'indan. Istekten alinsaydi
        // gecerli bir hesabi olan herkes baska bir sirkete bridge tanitabilirdi.
        if (CompanyOf(callerIdentity) is not { } companyId)
        {
            logger.LogWarning("Şirket bilgisi olmayan bir hesapla bridge kaydı denemesi.");
            return Results.BadRequest(new
            {
                error = "Hesabınızda şirket bilgisi yok; bridge kaydı yapılamaz."
            });
        }

        var bridgeId = Guid.NewGuid();
        var name = string.IsNullOrWhiteSpace(request.Name) ? request.MachineName : request.Name;

        // Kimlik Keycloak'ta aciliyor. Once orada, sonra defterde: Keycloak
        // adimi duserse elimizde kimligi olmayan bir defter kaydi kalmasin.
        BridgeCredentials credentials;
        try
        {
            credentials = await identity.CreateAsync(bridgeId, companyId, name, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bridge kimliği açılamadı. Şirket: {CompanyId}", companyId);
            return Results.Problem(
                detail: "Kimlik sunucusunda bridge hesabı açılamadı.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        await store.RegisterAsync(
            bridgeId, companyId, name, request.MachineName, request.BridgeVersion, ct);

        // Sir yalnizca burada, bir kez doner — Keycloak onu kendi sakliyor.
        return Results.Ok(new
        {
            bridgeId,
            companyId,
            clientId = credentials.ClientId,
            clientSecret = credentials.ClientSecret,
            tokenEndpoint = credentials.TokenEndpoint
        });
    }

    private static async Task<IResult> ListBridges(
        BridgeStore store,
        BridgeRegistry registry,
        IIdentityService identity,
        CancellationToken ct)
    {
        if (CompanyOf(identity) is not { } companyId)
            return Results.BadRequest(new { error = "Token'da şirket bilgisi yok." });

        var bridges = await store.ListAsync(companyId, ct);

        return Results.Ok(bridges.Select(bridge => new
        {
            bridge.Id,
            bridge.Name,
            bridge.MachineName,
            bridge.Version,
            bridge.CreatedAt,
            bridge.LastSeenAt,
            // "Çevrimiçi" bu ornege bagli olmak demek. Cok replikali calismada
            // bu bilgi eksik kalir; BridgeRegistry'deki nota bakin.
            online = registry.IsOnline(bridge.Id)
        }));
    }

    private static async Task<IResult> ListBindings(
        BridgeStore store,
        IIdentityService identity,
        CancellationToken ct)
    {
        if (CompanyOf(identity) is not { } companyId)
            return Results.BadRequest(new { error = "Token'da şirket bilgisi yok." });

        var bindings = await store.GetBindingsAsync(companyId, ct);

        return Results.Ok(bindings.Select(kv => new
        {
            connectionId = kv.Key,
            bridgeId = kv.Value
        }));
    }

    private static async Task<IResult> RevokeBridge(
        Guid bridgeId,
        BridgeStore store,
        KeycloakBridgeIdentity bridgeIdentity,
        IIdentityService identity,
        CancellationToken ct)
    {
        if (CompanyOf(identity) is not { } companyId)
            return Results.BadRequest(new { error = "Token'da şirket bilgisi yok." });

        var revoked = await store.RevokeAsync(bridgeId, companyId, ct);
        if (!revoked) return Results.NotFound();

        // Asil iptal burada: Keycloak kimligi kapatilmazsa bridge elindeki
        // token'la calismaya devam eder ve defterdeki "iptal" kaydi yalanci
        // bir guvence olurdu.
        await bridgeIdentity.DisableAsync(bridgeId, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> BindConnection(
        Guid connectionId,
        [FromBody] BindConnectionRequest request,
        BridgeStore store,
        DataAnalysisDbContext db,
        BridgeConnectionSync sync,
        BridgeRegistry registry,
        IIdentityService identity,
        CancellationToken ct)
    {
        if (CompanyOf(identity) is not { } companyId)
            return Results.BadRequest(new { error = "Token'da şirket bilgisi yok." });

        // Baglanti gercekten bu sirkete mi ait. Olmadan, baska bir sirketin
        // baglanti kimligini bilen biri onu kendi bridge'ine yonlendirebilirdi.
        var connection = await db.SavedConnections.FirstOrDefaultAsync(
            c => c.Id == connectionId && c.CompanyId == companyId, ct);

        if (connection is null) return Results.NotFound(new { error = "Bağlantı bulunamadı." });

        if (request.BridgeId is { } bridgeId)
        {
            var bridges = await store.ListAsync(companyId, ct);
            if (bridges.All(b => b.Id != bridgeId))
                return Results.BadRequest(new { error = "Bridge bulunamadı." });
        }

        // Onceki bridge'e "bu baglantiyi unut" demek gerekiyor: sifrenin
        // artik kullanilmayan bir bridge'in diskinde kalmasi, moddan
        // cikmanin yarim kalmis hali olurdu.
        var previousBridgeId = await store.GetBoundBridgeAsync(connectionId, ct);

        await store.BindConnectionAsync(connectionId, companyId, request.BridgeId, ct);

        if (previousBridgeId is { } previous && previous != request.BridgeId)
            await sync.ForgetAsync(connectionId, previous, companyId, registry, ct);

        // Tanimi yeni bridge'e gonder. Bridge cevrimdisiyse bu sessizce
        // atlanir; baglandiginda hub zaten hepsini yolluyor.
        if (request.BridgeId is { } target)
            await sync.SyncOneAsync(connectionId, target, companyId, registry, ct);

        return Results.Ok(new
        {
            connectionId,
            mode = request.BridgeId is null ? "direct" : "bridge",
            bridgeId = request.BridgeId
        });
    }
}

public record EnrollRequest(
    string MachineName,
    string BridgeVersion,
    string ProtocolVersion,
    string? Name = null);

public record BindConnectionRequest(Guid? BridgeId);

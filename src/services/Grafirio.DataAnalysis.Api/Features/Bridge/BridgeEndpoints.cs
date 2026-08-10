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
///   1. Sirket yoneticisi panelden tek kullanimlik token uretir.
///   2. Installer bu token'i ister, <c>/enroll</c>'a gonderir.
///   3. Bridge kendi kimligini ve uzun omurlu sirrini alir, diske sifreli yazar.
///
/// Kurulum dosyasinin kendisi herkese acik durabilir; bir bridge'i sirkete
/// baglayan sey token.
/// </summary>
public static class BridgeEndpoints
{
    public static void MapBridgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bridges")
            .WithTags("Bridge")
            .WithOpenApi();

        // Kayit ucu kimlik dogrulamasi ISTEMEZ: bridge henuz bir kimlige sahip
        // degil. Yetkiyi token veriyor ve token tek kullanimlik.
        group.MapPost("/enroll", Enroll)
            .AllowAnonymous()
            .WithDescription("Bridge'i kayıt token'ıyla sisteme tanıtır");

        var managed = group.MapGroup("").RequireAuthorization("CompanyAccess");

        managed.MapPost("/enrollment-tokens", CreateEnrollmentToken)
            .WithDescription("Yeni bir bridge kurulumu için tek kullanımlık token üretir");

        managed.MapGet("/", ListBridges)
            .WithDescription("Şirketin bridge'lerini ve çevrimiçi durumlarını listeler");

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

    private static async Task<IResult> CreateEnrollmentToken(
        BridgeStore store,
        IIdentityService identity,
        CancellationToken ct)
    {
        if (CompanyOf(identity) is not { } companyId)
            return Results.BadRequest(new { error = "Token'da şirket bilgisi yok." });

        var token = await store.CreateEnrollmentTokenAsync(
            companyId, identity.UserId.ToString(), ct);

        return Results.Ok(new
        {
            token,
            // Kullaniciya sureyi soylemek gerekiyor: token'i bir kenara yazip
            // ertesi gun kurmaya calismak yaygin ve o an sebebi anlasilmiyor.
            expiresInMinutes = (int)BridgeStore.EnrollmentTokenLifetime.TotalMinutes,
            protocolVersion = BridgeProtocol.Version
        });
    }

    private static async Task<IResult> Enroll(
        EnrollRequest request,
        BridgeStore store,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("BridgeEnrollment");

        if (string.IsNullOrWhiteSpace(request.Token))
            return Results.BadRequest(new { error = "Kayıt token'ı boş." });

        if (request.ProtocolVersion != BridgeProtocol.Version)
            return Results.BadRequest(new
            {
                error = $"Bu bridge sürümü sunucuyla uyumlu değil " +
                        $"(bridge: {request.ProtocolVersion}, sunucu: {BridgeProtocol.Version}). " +
                        "Lütfen güncel kurulum dosyasını indirin."
            });

        var companyId = await store.RedeemEnrollmentTokenAsync(request.Token, ct);

        if (companyId is null)
        {
            logger.LogWarning("Geçersiz ya da süresi dolmuş kayıt token'ı ile deneme.");
            return Results.BadRequest(new
            {
                error = "Kayıt token'ı geçersiz, süresi dolmuş ya da daha önce kullanılmış."
            });
        }

        var (bridgeId, secret) = await store.RegisterAsync(
            companyId,
            string.IsNullOrWhiteSpace(request.Name) ? request.MachineName : request.Name,
            request.MachineName,
            request.BridgeVersion,
            ct);

        // Sir yalnizca burada, bir kez doner. Sunucuda ozeti duruyor.
        return Results.Ok(new
        {
            bridgeId,
            secret,
            companyId
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
        IIdentityService identity,
        CancellationToken ct)
    {
        if (CompanyOf(identity) is not { } companyId)
            return Results.BadRequest(new { error = "Token'da şirket bilgisi yok." });

        var revoked = await store.RevokeAsync(bridgeId, companyId, ct);
        return revoked ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> BindConnection(
        Guid connectionId,
        [FromBody] BindConnectionRequest request,
        BridgeStore store,
        DataAnalysisDbContext db,
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

        await store.BindConnectionAsync(connectionId, companyId, request.BridgeId, ct);

        return Results.Ok(new
        {
            connectionId,
            mode = request.BridgeId is null ? "direct" : "bridge",
            bridgeId = request.BridgeId
        });
    }
}

public record EnrollRequest(
    string Token,
    string MachineName,
    string BridgeVersion,
    string ProtocolVersion,
    string? Name = null);

public record BindConnectionRequest(Guid? BridgeId);

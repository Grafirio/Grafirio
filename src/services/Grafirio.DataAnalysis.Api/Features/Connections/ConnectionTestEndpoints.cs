using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Models;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

/// <summary>
/// Baglanti denemesi: kayitli bir baglantiya gercekten ulasilabiliyor mu diye
/// bakar. Kayitli baglantilarin CRUD'u <see cref="SavedConnectionEndpoints"/>'te.
///
/// <b>Uc artik kayitli baglanti kimligi aliyor, ham kimlik bilgisi degil.</b>
/// Eskiden govdede host/kullanici/sifre geliyordu ve bunun iki sonucu vardi:
///
///   * Test bridge'i kullanamiyordu. Yol secimi baglantinin bridge esleşmesine
///     bagli, esleşme de kimligine; govdedeki ham bilgide kimlik yok. Yani
///     firewall arkasindaki her veritabani icin test HER ZAMAN basarisizdi.
///     Arayuz kaydi teste bagladigi icin o baglantilar hic kaydedilemiyordu —
///     bridge kurulu ve cevrimici olsa bile.
///   * Sunucu, istekte yazan herhangi bir adrese baglanti kuruyordu. Kimlik
///     dogrulamasi eklenmisti ama gecerli bir hesabi olan herkes bunu ic
///     aglari yoklamak icin kullanabiliyordu; kaydedilmis baglantilarla
///     sinirlamak bu yuzeyi tamamen kapatiyor.
///
/// Akis artik tek yonlu: kaydet → (istege bagli) bridge'e bagla → test et.
/// </summary>
public static class ConnectionTestEndpoints
{
    public static void MapConnectionTestEndpoints(this IEndpointRouteBuilder app)
    {
        // Politika adli: paylasilan kurulum AddAuthentication()'i varsayilan
        // sema vermeden cagiriyor, ciplak RequireAuthorization() burada
        // "No authenticationScheme was specified" ile 400 donuyor.
        var group = app.MapGroup("/api/connections")
            .RequireAuthorization("CompanyAccess")
            .WithTags("Connection Management")
            .WithOpenApi();

        // Test okuma islemi: baglantiyi degistirmiyor, yalnizca ulasilabilir
        // mi diye bakiyor.
        group.MapPost("/{connectionId:guid}/test", TestConnection)
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithName("TestConnection")
            .WithDescription("Kayıtlı bağlantıya ulaşılabiliyor mu diye bakar (bridge varsa onun üzerinden)");
    }

    private static async Task<IResult> TestConnection(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        [FromServices] IDataSourceFactory dataSources,
        CancellationToken ct)
    {
        var (connection, error) = await ConnectionScope.ResolveAsync(db, identity, connectionId, ct);
        if (error is not null) return error;

        var result = await dataSources.ProbeAsync(connection!, ct);

        // Basarili bir deneme "en son ne zaman ulastik" bilgisini tazeliyor.
        // Panelde bu alan vardi ama yalnizca /decrypt cagrisinda guncelleniyordu
        // — yani sifreyi okumak "baglandik" sayiliyor, gercekten baglanmak
        // sayilmiyordu.
        if (result.Success)
        {
            connection!.LastConnectedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new TestConnectionResponse(
            Success: result.Success,
            Message: result.Message,
            ConnectionId: connectionId.ToString()
        ));
    }
}

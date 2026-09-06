using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Models;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

/// <summary>
/// Tests saved connections using their persisted route and exposes isolated connection previews.
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

        group.MapConnectionPreviewEndpoints();

        // Test okuma islemi: baglantiyi degistirmiyor, yalnizca ulasilabilir
        // mi diye bakiyor.
        group.MapPost("/{connectionId:guid}/test", TestConnection)
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithName("TestConnection")
            .WithDescription("Tests connectivity using the saved connection route.");
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

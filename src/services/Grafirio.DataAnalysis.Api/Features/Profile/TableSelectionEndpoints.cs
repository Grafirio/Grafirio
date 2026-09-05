using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Grafirio.QueryPolicy;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>
/// Baglantiya ait tablo secimi.
///
/// Bu secim onceden yalnizca tarayicinin localStorage'inda tutuluyordu; sunucu
/// hangi tablolarin secildigini hic bilmiyordu. Dolayisiyla "yalnizca secili
/// tablolar islenir" kurali teknik olarak uygulanamiyordu ve sema cikarma
/// veritabanindaki her tabloyu tariyordu.
/// </summary>
public static class TableSelectionEndpoints
{
    public static void MapTableSelectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connections/{connectionId:guid}/tables")
            // Politika adli: paylasilan kurulumda varsayilan sema yok.
            .RequireAuthorization("CompanyAccess")
            .WithTags("Table Selection")
            .WithOpenApi();

        group.MapPut("/", SaveSelection)
            .RequirePermission(AppPermissions.DataSourcesUpdate)
            .WithName("SaveSelectedTables")
            .WithDescription("Bağlantı için analiz edilecek tabloları kaydeder");

        group.MapGet("/", GetSelection)
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithName("GetSelectedTables")
            .WithDescription("Bağlantı için kayıtlı tablo seçimini döndürür");
    }

    private static async Task<IResult> SaveSelection(
        Guid connectionId,
        SelectedTablesRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ConnectionProfileStore store,
        [FromServices] IDataSourceFactory dataSources,
        [FromServices] IIdentityService identity,
        [FromServices] BridgeConnectionSync sync,
        [FromServices] BridgeRegistry registry,
        [FromServices] ILogger<SelectedTablesRequest> logger,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        // Baglanti gercekten bu sirkete mi ait — kimligi istekten degil
        // token'dan aliyoruz ve kaydi da onunla suzuyoruz.
        var connection = await db.SavedConnections.AsNoTracking().FirstOrDefaultAsync(
            c => c.Id == connectionId && c.CompanyId == scopedCompanyId && c.IsActive, ct);

        // Bulunamadi icin 404: 403 "bu kayit var ama senin degil" bilgisini sizdirir.
        if (connection is null) return Results.NotFound(new { error = "Bağlantı bulunamadı" });

        IReadOnlyList<string> tables;
        try
        {
            tables = QueryTableScope.Normalize(request.Tables);
            // Catalog-only bootstrap works before the connection has any selected customer tables.
            await using var session = await dataSources.OpenAsync(connection, ct);
            var baseTables = await session.QueryRowsAsync(QueryTableScope.BaseTablesSql, ct: ct);
            QueryTableScope.RequireBaseTables(tables, baseTables.Select(row => new SqlTableIdentity(
                row.GetRequiredString("SchemaName"), row.GetRequiredString("TableName"))));
        }
        catch (QueryPolicyException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (DataSourceException ex)
        {
            logger.LogWarning(ex, "Base table verification failed for connection {ConnectionId}.", connectionId);
            return Results.Problem(title: "Table selection could not be verified.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        await store.SaveSelectedTablesAsync(connectionId, scopedCompanyId, tables, ct);

        // Secim degistiginde eski analiz gecersizdir: sozluk artik secili
        // olmayan tablolari anlatiyor ya da yeni secilenleri hic tanimiyor.
        // Bunu yapmazsak baglanti "ready" gorunmeye devam eder ve sorgu ucu
        // eskimis bir sozlukle kolon secmeye calisir.
        var stale = await db.AnalysisConfigs
            .Where(c => c.ConnectionId == connectionId && c.CompanyId == scopedCompanyId && c.IsActive)
            .ToListAsync(ct);

        foreach (var config in stale) config.IsActive = false;
        if (stale.Count > 0) await db.SaveChangesAsync(ct);

        // Secim ayni zamanda bridge'in izin listesi: bridge, listede olmayan
        // bir tabloya giden sorguyu bulut ne gonderirse gondersin reddediyor.
        // Yeni secim gonderilmezse bridge eski listeyle calismaya devam eder —
        // yani yeni secilen tablo, sebebi arayuzde gorunmeden reddedilir.
        //
        // Bu tetikleyici eskiden baglanti–bridge eslestirmesine bagliydi; o
        // kavram kalkinca buraya tasindi.
        try
        {
            await sync.SyncOneAsync(connectionId, scopedCompanyId, registry, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Table selection could not be sent to bridge for {ConnectionId}. It will be retried on reconnect.",
                connectionId);
        }

        return Results.Ok(new
        {
            success = true,
            connectionId,
            tables,
            // Secim degistiginde analiz gecersiz olur; arayuz kullaniciyi
            // yeniden "Analiz Et"e yonlendirsin diye durumu da donuyoruz.
            status = "none"
        });
    }

    private static async Task<IResult> GetSelection(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ConnectionProfileStore store,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        var exists = await db.SavedConnections.AnyAsync(
            c => c.Id == connectionId && c.CompanyId == scopedCompanyId && c.IsActive, ct);

        if (!exists) return Results.NotFound(new { error = "Bağlantı bulunamadı" });

        var tables = await store.GetSelectedTablesAsync(connectionId, scopedCompanyId, ct);

        var status = await db.AnalysisConfigs
            .Where(c => c.ConnectionId == connectionId && c.CompanyId == scopedCompanyId && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => c.Status)
            .FirstOrDefaultAsync(ct) ?? "none";

        return Results.Ok(new { success = true, connectionId, tables, status });
    }
}

public record SelectedTablesRequest(List<string>? Tables);

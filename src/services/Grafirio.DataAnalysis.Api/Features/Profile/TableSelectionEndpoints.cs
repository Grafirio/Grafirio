using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Mongo;
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
            .WithName("SaveSelectedTables")
            .WithDescription("Bağlantı için analiz edilecek tabloları kaydeder");

        group.MapGet("/", GetSelection)
            .WithName("GetSelectedTables")
            .WithDescription("Bağlantı için kayıtlı tablo seçimini döndürür");
    }

    private static async Task<IResult> SaveSelection(
        Guid connectionId,
        SelectedTablesRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ConnectionProfileStore store,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        // Baglanti gercekten bu sirkete mi ait — kimligi istekten degil
        // token'dan aliyoruz ve kaydi da onunla suzuyoruz.
        var exists = await db.SavedConnections.AnyAsync(
            c => c.Id == connectionId && c.CompanyId == scopedCompanyId && c.IsActive, ct);

        // Bulunamadi icin 404: 403 "bu kayit var ama senin degil" bilgisini sizdirir.
        if (!exists) return Results.NotFound(new { error = "Bağlantı bulunamadı" });

        var tables = (request.Tables ?? [])
            .Select(t => t?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tables.Count == 0)
            return Results.BadRequest(new { error = "En az bir tablo seçilmeli." });

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

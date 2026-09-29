using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Admin;

/// <summary>
/// Platform yonetimi (ProjectAdmin) icin semantik zeka sinyalleri.
///
/// Butun sirketler uzerinden TOPLU rakam donuyor; sirket, kullanici, soru metni
/// ya da tablo adi yok. Yine de yalnizca Grafirio personeline (PLATFORM_ADMIN)
/// acik: toplu rakamlar bile musteri kullanimini ele verir.
/// </summary>
public static class SemanticSignalEndpoints
{
    /// <summary>Identity servisindeki PlatformRoles.PLATFORM_ADMIN ile ayni deger.</summary>
    public const string PlatformAdminRole = "PLATFORM_ADMIN";

    public const int MaxDays = 365;

    public static void MapSemanticSignalEndpoints(this IEndpointRouteBuilder app)
    {
        // "Password": oturum acmis herhangi bir kullanici; rol kontrolu ucun
        // icinde. Platform yoneticisinin token'inda sirket olmayabilir, bu yuzden
        // CompanyAccess kullanilamiyor.
        app.MapGet("/api/admin/semantic-signals", GetSignals)
            .RequireAuthorization("Password")
            .WithTags("Admin")
            .WithName("GetSemanticSignals")
            .WithDescription("Semantik zeka yeteneklerinin üretimdeki dolaylı sinyalleri (yalnızca toplu, platform yöneticisi)");
    }

    public static async Task<IResult> GetSignals(
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        [FromServices] IServiceProvider services,
        [FromServices] ILoggerFactory loggers,
        CancellationToken ct,
        [FromQuery] int days = 30)
    {
        if (!identity.HasBusinessRole(PlatformAdminRole)) return Results.Forbid();
        days = Math.Clamp(days, 1, MaxDays);

        var to = DateTime.UtcNow;
        var from = to.Date.AddDays(-days + 1);

        var rows = await db.QueryHistories.AsNoTracking()
            .Where(q => q.CreatedAt >= from)
            .Select(q => new
            {
                q.Id, q.ConfigId, q.UserId, q.Question, q.Status, q.CreatedAt, q.ParentQueryId, q.FeedbackRating,
                q.ChartTypeOverride,
                // Hata metni yalnizca basarisiz satirlardan; sonuc govdeleri buyuk.
                ResultJson = q.Status == "failed" ? q.ResultJson : null
            })
            .ToListAsync(ct);

        var questions = rows.Select(r => new SignalQuestion(r.Id, r.ConfigId, r.UserId, r.Question, r.Status,
            r.CreatedAt, r.ParentQueryId, r.FeedbackRating, r.ChartTypeOverride, ErrorOf(r.ResultJson))).ToList();

        var analyses = (await db.AnalysisConfigs.AsNoTracking()
                .Where(a => a.CreatedAt >= from)
                .Select(a => new { a.Status, a.CreatedAt, a.ConfigJson })
                .ToListAsync(ct))
            .Select(a => new SignalAnalysis(a.Status, a.CreatedAt, a.ConfigJson))
            .ToList();

        // Ogrenilmis bilgiler Mongo'da. Mongo yapilandirilmamissa diger sinyaller
        // yine donmeli; eksik kaynak rapora "facts: unavailable" diye yaziliyor.
        List<(string Kind, bool Accepted, long Count)> facts = [];
        var factsAvailable = true;
        try
        {
            facts = await services.GetRequiredService<LearnedFactStore>().CountSinceAsync(from, ct);
        }
        catch (Exception exception) when (exception is InvalidOperationException or MongoDB.Driver.MongoException
                                              or TimeoutException)
        {
            factsAvailable = false;
            loggers.CreateLogger(typeof(SemanticSignalEndpoints))
                .LogWarning(exception, "Ogrenilmis bilgiler okunamadi; semantik sinyaller onlarsiz donuyor");
        }

        var report = SemanticSignals.Compute(questions, analyses, facts, from, to);
        return Results.Ok(new { report, days, factsAvailable });
    }

    private static string? ErrorOf(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

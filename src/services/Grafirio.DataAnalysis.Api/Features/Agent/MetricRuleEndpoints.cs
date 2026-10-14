using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;
using Microsoft.EntityFrameworkCore;
using static Grafirio.DataAnalysis.Api.Features.Agent.AgentAnalyzeEndpoints;
using Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

public static class MetricRuleEndpoints
{
    public sealed record MetricRule(string Table, string Column, string Label, string Unit,
        string[] AllowedAggregations, string? DateColumn, bool ForecastEnabled,
        string MissingPeriodPolicy = "reject", int MinHistoryPeriods = 12, int MaxHorizon = 6);
    public sealed record UpdateRules(Guid ConfigId, string DictionaryHash, List<MetricRule> Rules);

    public static void MapMetricRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent/config").RequireAuthorization("CompanyAccess");
        group.MapGet("/{connectionId:guid}/metric-rules", async (Guid connectionId,
            DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct) =>
        {
            if (identity.CurrentCompanyId is not { } company) return Results.Forbid();
            var companyId = company.ToString();
            var config = await db.AnalysisConfigs.AsNoTracking().Where(c => c.ConnectionId == connectionId
                && c.CompanyId == companyId && c.IsActive
                && db.SavedConnections.Any(s => s.Id == connectionId && s.CompanyId == companyId && s.IsActive))
                .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
            if (config is null) return Results.NotFound();
            var dictionary = JsonNode.Parse(config.ConfigJson)!;
            return Results.Ok(new { configId = config.Id, dictionaryHash = QueryDiagnostics.Hash(config.ConfigJson), columns = dictionary["columns"],
                rules = dictionary["metricRules"] ?? new JsonArray(), warnings = dictionary["metricRuleWarnings"] ?? new JsonArray() });
        }).RequirePermission(AppPermissions.DataSourcesRead);

        group.MapPut("/{connectionId:guid}/metric-rules", async (Guid connectionId, UpdateRules request,
            DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct) =>
        {
            if (identity.CurrentCompanyId is not { } company) return Results.Forbid();
            var companyId = company.ToString();
            var config = await db.AnalysisConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ConfigId
                && c.ConnectionId == connectionId && c.CompanyId == companyId && c.IsActive
                && c.Status == AnalysisStatus.Ready
                && db.SavedConnections.Any(s => s.Id == connectionId && s.CompanyId == companyId && s.IsActive), ct);
            if (config is null) return Results.Conflict(new { error = "Bağlantı hazır değil veya sözlük değişti. Yenileyin." });
            if (request.DictionaryHash != QueryDiagnostics.Hash(config.ConfigJson))
                return Results.Conflict(new { error = "Kuralları açtıktan sonra sözlük değişti. Ekranı kapatıp yeniden açın." });
            var dictionary = JsonNode.Parse(config.ConfigJson)!.AsObject();
            try { dictionary["metricRules"] = ValidateRules(dictionary, request.Rules); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            var json = dictionary.ToJsonString();
            var written = await db.AnalysisConfigs.Where(c => c.Id == config.Id && c.IsActive
                && c.CompanyId == companyId && c.ConfigJson == config.ConfigJson && c.Status == AnalysisStatus.Ready
                && db.SavedConnections.Any(s => s.Id == connectionId && s.CompanyId == companyId && s.IsActive))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConfigJson, json)
                    .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), ct);
            return written == 1 ? Results.Ok(new { rules = dictionary["metricRules"] })
                : Results.Conflict(new { error = "Sözlük değişti; yenileyip tekrar kaydedin." });
        }).RequirePermission(AppPermissions.DataSourcesUpdate);
    }

    public static JsonArray ValidateRules(JsonObject dictionary, List<MetricRule>? rules)
    {
        if (rules is null || rules.Count > 100) throw new ArgumentException("En fazla 100 metrik tanımlanabilir.");
        var columns = (dictionary["columns"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var result = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var column = columns.FirstOrDefault(c => string.Equals(c["table"]?.ToString(), rule.Table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c["column"]?.ToString(), rule.Column, StringComparison.OrdinalIgnoreCase));
            if (column is null || column["role"]?.ToString() == "identifier")
                throw new ArgumentException("Metrik sözlükte bulunmalı ve kimlik alanı olmamalı.");
            if (!seen.Add(rule.Table + "." + rule.Column)) throw new ArgumentException("Aynı alan için birden fazla kural olamaz.");
            if (string.IsNullOrWhiteSpace(rule.Label) || rule.Label.Length > 120
                || string.IsNullOrWhiteSpace(rule.Unit) || rule.Unit.Length > 40)
                throw new ArgumentException("Metrik adı ve birimi gereklidir (en fazla 120 ve 40 karakter).");
            if (rule.AllowedAggregations is not { Length: > 0 and <= 5 }
                || rule.AllowedAggregations.Any(a => a is not ("sum" or "count" or "avg" or "min" or "max")))
                throw new ArgumentException("İzinli işlemler: sum, count, avg, min, max.");
            if (rule.AllowedAggregations.Any(a => a != "count")
                && !System.Text.RegularExpressions.Regex.IsMatch(column["dataType"]?.ToString() ?? "", "int|decimal|numeric|money|float|real|double", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new ArgumentException("Toplam ve benzeri işlemler için sayısal bir alan seçin.");
            if (rule.MissingPeriodPolicy is not ("reject" or "zero") || rule.MinHistoryPeriods is < 4 or > 120
                || rule.MaxHorizon is < 1 or > 36)
                throw new ArgumentException("Eksik dönem kuralı, geçmiş uzunluğu veya tahmin ufku geçersiz.");
            var date = columns.FirstOrDefault(c => string.Equals(c["table"]?.ToString(), rule.Table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c["column"]?.ToString(), rule.DateColumn, StringComparison.OrdinalIgnoreCase));
            if (date is not null && date["role"]?.ToString() != "date"
                && !System.Text.RegularExpressions.Regex.IsMatch(date["dataType"]?.ToString() ?? "", "date|time", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new ArgumentException("Seçilen zaman alanı tarih türünde olmalı.");
            if ((!string.IsNullOrEmpty(rule.DateColumn) && date is null) || (rule.ForecastEnabled && date is null))
                throw new ArgumentException("Tahmin için aynı tablodan bir tarih alanı seçin.");
            result.Add(new JsonObject {
                ["table"] = column["table"]!.DeepClone(), ["column"] = column["column"]!.DeepClone(),
                ["label"] = rule.Label.Trim(), ["unit"] = rule.Unit.Trim(),
                ["allowedAggregations"] = new JsonArray(rule.AllowedAggregations.Distinct().Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
                ["dateColumn"] = date?["column"]?.DeepClone(), ["forecastEnabled"] = rule.ForecastEnabled,
                ["missingPeriodPolicy"] = rule.MissingPeriodPolicy, ["minHistoryPeriods"] = rule.MinHistoryPeriods,
                ["maxHorizon"] = rule.MaxHorizon, ["source"] = "user", ["updatedAt"] = DateTime.UtcNow });
        }
        return result;
    }
}

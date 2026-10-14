using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.QueryPolicy;
using Grafirio.Shared.Identity.Services;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Admin;

public static class DiagnosticEndpoints
{
    public static void MapDiagnosticEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/diagnostics").RequireAuthorization("Password").WithTags("Diagnostics");
        // Every handler checks the platform role, including mutations and detail endpoints.
        group.MapGet("", List);
        group.MapGet("/summary", Summary);
        group.MapGet("/settings", GetSettings);
        group.MapPut("/settings", PutSettings);
        group.MapGet("/admin-events", AdminEvents);
        group.MapGet("/tests", Tests);
        group.MapPost("/tests", CreateTest);
        group.MapPut("/tests/{id:guid}", UpdateTest);
        group.MapDelete("/tests/{id:guid}", DeleteTest);
        group.MapPost("/tests/{id:guid}/run", RunTest);
        group.MapGet("/{id:guid}", Detail);
        group.MapPut("/{id:guid}/review", Review);
    }

    private static bool Allowed(IIdentityService identity) => identity.HasBusinessRole(SemanticSignalEndpoints.PlatformAdminRole);
    private const int SampleLimit = 5000;
    public sealed record View(Guid Id, Guid? QueryId, Guid ConnectionId, string CompanyId, string Question,
        DateTime CreatedAt, DateTime? CompletedAt, string Status, string Stage, string? ErrorCode, string? Error,
        string Model, string PromptVersion, string BuildVersion, string DictionaryHash, string ContextHash,
        int DurationMs, short? FeedbackRating, string ReviewStatus, string ReviewNote, bool Legacy,
        JsonNode? Parameters, JsonNode? Context, JsonNode? Audit, JsonNode? Events);

    public static View ToView(QueryDiagnostic row, QueryHistory? query = null)
    {
        var result = QueryDiagnostics.Parse(query?.ResultJson) as JsonObject;
        var error = result?["error"]?.ToString() ?? row.Error;
        var status = query?.Status ?? row.Status;
        // A crash between receiving and finishing remains visible instead of looking indefinitely active.
        if (query is null && status == "preparing" && DateTime.UtcNow - row.CreatedAt > TimeSpan.FromMinutes(15))
        { status = "failed"; error = "İstek hazırlık aşamasında tamamlanmadı; servis kesintisi veya bağlantı iptali olabilir."; }
        var code = status == "failed" ? result?["errorCode"]?.ToString() ?? row.ErrorCode ?? QueryDiagnostics.Classify(error, row.Stage) : null;
        var duration = query?.CompletedAt is { } finished
            ? (int)Math.Clamp((finished - query.CreatedAt).TotalMilliseconds + (query.PreparationMs ?? 0), 0, int.MaxValue)
            : query is not null && status is "processing" or "queued" or "cancelling"
                ? (int)Math.Clamp((DateTime.UtcNow - query.CreatedAt).TotalMilliseconds + (query.PreparationMs ?? 0), 0, int.MaxValue) : row.DurationMs;
        return new(row.Id, query?.Id ?? row.QueryId, row.ConnectionId, row.CompanyId, QueryDiagnostics.Mask(row.Question),
            row.CreatedAt, query is null ? row.CompletedAt : query.CompletedAt, status, row.Stage,
            code, QueryDiagnostics.SafeText(error), row.Model, row.PromptVersion, row.BuildVersion, row.DictionaryHash,
            row.ContextHash, duration, query?.FeedbackRating, row.ReviewStatus, QueryDiagnostics.Mask(row.ReviewNote),
            row.Model == "legacy", QueryDiagnostics.Parse(QueryDiagnostics.SafeJson(query?.PyCaretParamsJson ?? row.ParametersJson)),
            QueryDiagnostics.Parse(row.ContextJson), QueryDiagnostics.Parse(QueryDiagnostics.SafeJson(result?["audit"]?.ToJsonString() ?? "{}")),
            QueryDiagnostics.Parse(row.EventsJson));
    }

    private static async Task<List<View>> Load(DataAnalysisDbContext db, int days, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        var rows = await db.QueryDiagnostics.AsNoTracking().Where(d => d.CreatedAt >= since)
            .OrderByDescending(d => d.CreatedAt).Take(SampleLimit).ToListAsync(ct);
        var ids = rows.Where(r => r.QueryId.HasValue).Select(r => r.QueryId!.Value).ToList();
        var queries = await db.QueryHistories.AsNoTracking().Where(q => ids.Contains(q.Id))
            .Select(q => new QueryHistory { Id = q.Id, Status = q.Status, CreatedAt = q.CreatedAt, CompletedAt = q.CompletedAt,
                PreparationMs = q.PreparationMs, FeedbackRating = q.FeedbackRating, ResultJson = q.Status == "failed" ? q.ResultJson : "{}" })
            .ToDictionaryAsync(q => q.Id, ct);
        var views = rows.Select(r => ToView(r, r.QueryId is { } id ? queries.GetValueOrDefault(id) : null)).ToList();
        // Existing records remain inspectable; missing historical context is explicitly labelled.
        var legacy = await (from q in db.QueryHistories.AsNoTracking()
            join c in db.AnalysisConfigs.AsNoTracking() on q.ConfigId equals c.Id
            where q.CreatedAt >= since && !db.QueryDiagnostics.Any(d => d.QueryId == q.Id)
            orderby q.CreatedAt descending
            select new { Query = new QueryHistory { Id = q.Id, Question = q.Question, Status = q.Status, CreatedAt = q.CreatedAt,
                CompletedAt = q.CompletedAt, PreparationMs = q.PreparationMs, FeedbackRating = q.FeedbackRating,
                ResultJson = q.Status == "failed" ? q.ResultJson : "{}" }, c.ConnectionId, c.CompanyId }).Take(SampleLimit).ToListAsync(ct);
        views.AddRange(legacy.Select(x => ToView(new QueryDiagnostic
        {
            Id = x.Query.Id, QueryId = x.Query.Id, ConnectionId = x.ConnectionId, CompanyId = x.CompanyId,
            Question = x.Query.Question, CreatedAt = x.Query.CreatedAt, Status = x.Query.Status, Model = "legacy",
            Stage = "execution", CompletedAt = x.Query.CompletedAt
        }, x.Query)));
        return views.OrderByDescending(x => x.CreatedAt).Take(SampleLimit).ToList();
    }

    public static async Task<IResult> List(DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct,
        int days = 30, int page = 1, string? status = null, string? errorCode = null, string? search = null, string? companyId = null, Guid? connectionId = null)
    {
        if (!Allowed(identity)) return Results.Forbid();
        List<View> rows;
        if (Guid.TryParse(search, out var lookup))
        {
            // Tracking IDs must remain discoverable outside the overview sample/date window.
            var diagnosticId = await db.QueryDiagnostics.AsNoTracking().Where(d => d.Id == lookup || d.QueryId == lookup)
                .Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct) ?? lookup;
            var found = await Detail(diagnosticId, db, identity, ct);
            rows = (found as IValueHttpResult)?.Value is View view ? [view] : [];
        }
        else rows = await Load(db, days, ct);
        var filtered = rows.Where(r => (string.IsNullOrEmpty(status) || r.Status == status)
            && (string.IsNullOrEmpty(errorCode) || r.ErrorCode == errorCode)
            && (string.IsNullOrEmpty(companyId) || r.CompanyId == companyId)
            && (connectionId is null || r.ConnectionId == connectionId)
            && (string.IsNullOrWhiteSpace(search) || r.Question.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) || r.QueryId?.ToString() == search)).ToList();
        page = Math.Max(1, page);
        return Results.Ok(new { items = filtered.Skip((page - 1) * 50).Take(50).Select(r => r with { Context = null, Audit = null, Parameters = null, Events = null }),
            total = filtered.Count, page, pageSize = 50, sampled = rows.Count >= SampleLimit, sampleLimit = SampleLimit });
    }

    public static async Task<IResult> Detail(Guid id, DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct)
    {
        if (!Allowed(identity)) return Results.Forbid();
        var row = await db.QueryDiagnostics.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is not null)
        {
            var query = row.QueryId is { } queryId ? await db.QueryHistories.AsNoTracking().FirstOrDefaultAsync(q => q.Id == queryId, ct) : null;
            return Results.Ok(ToView(row, query));
        }
        var legacy = await db.QueryHistories.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, ct);
        var config = legacy is null ? null : await db.AnalysisConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == legacy.ConfigId, ct);
        return legacy is null || config is null ? Results.NotFound() : Results.Ok(ToView(new QueryDiagnostic
        { Id = id, QueryId = id, Question = legacy.Question, ConnectionId = config.ConnectionId, CompanyId = config.CompanyId,
            CreatedAt = legacy.CreatedAt, Model = "legacy", Stage = "execution" }, legacy));
    }

    public static async Task<IResult> Summary(DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct, int days = 30)
    {
        if (!Allowed(identity)) return Results.Forbid();
        var rows = await Load(db, days, ct);
        var settings = await ReadSettings(db, ct);
        var terminal = rows.Where(r => r.Status is "completed" or "failed" or "clarification" or "cancelled").ToList();
        var durations = terminal.Select(r => r.DurationMs).Order().ToArray();
        var errors = rows.Where(r => r.ErrorCode is not null).GroupBy(r => r.ErrorCode)
            .Select(g => new { code = g.Key, count = g.Count(), ids = g.Take(10).Select(r => r.Id) }).ToList();
        var inconsistent = rows.Where(r => !string.IsNullOrEmpty(r.ContextHash))
            .GroupBy(r => new { r.CompanyId, r.ConnectionId, r.ContextHash })
            .Where(g => g.Any(r => r.Status == "completed") && g.Any(r => r.Status == "failed"))
            .Select(g => new { question = g.First().Question, count = g.Count(), ids = g.Where(r => r.Status == "completed").Take(1)
                .Concat(g.Where(r => r.Status == "failed").Take(1)).Select(r => r.Id) }).ToList();
        return Results.Ok(new
        {
            total = rows.Count, terminalCount = terminal.Count, completed = rows.Count(r => r.Status == "completed"),
            failed = rows.Count(r => r.Status == "failed"), emptyResults = rows.Count(r => r.ErrorCode == "empty_result"),
            successRate = rows.Count(r => r.Status is "completed" or "failed") == 0 ? (double?)null
                : (double)rows.Count(r => r.Status == "completed") / rows.Count(r => r.Status is "completed" or "failed"),
            clarification = rows.Count(r => r.Status == "clarification"), negativeFeedback = rows.Count(r => r.FeedbackRating == -1),
            ratedCount = rows.Count(r => r.FeedbackRating.HasValue),
            p95Ms = durations.Length == 0 ? (int?)null : durations[(int)Math.Ceiling(durations.Length * .95) - 1],
            errors, inconsistent,
            negativeQueries = rows.Where(r => r.FeedbackRating == -1).Take(20).Select(r => new { r.Id, r.Question }),
            repeatedQuestions = rows.GroupBy(r => new { r.CompanyId, r.ConnectionId, Question = r.Question.Trim().ToLowerInvariant() })
                .Where(g => g.Count() >= settings.RepeatedErrorThreshold && g.Any(r => r.Status == "failed" || r.FeedbackRating == -1))
                .Take(20).Select(g => new { question = g.First().Question, count = g.Count(), ids = g.Take(10).Select(r => r.Id) }),
            repeatedErrors = errors.Where(g => g.count >= settings.RepeatedErrorThreshold),
            slowQueries = rows.Where(r => r.DurationMs > settings.SlowQuerySeconds * 1000).Take(20).Select(r => new { r.Id, r.Question, r.DurationMs }),
            sampled = rows.Count >= SampleLimit, sampleLimit = SampleLimit, days = Math.Clamp(days, 1, 365)
        });
    }

    public sealed record ReviewRequest(string Status, string Note);
    public static async Task<IResult> Review(Guid id, ReviewRequest request, DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct)
    {
        if (!Allowed(identity)) return Results.Forbid();
        if (request.Status is not ("new" or "investigating" or "fixed" or "verified") || request.Note is null || request.Note.Length > 2000)
            return Results.BadRequest(new { error = "Geçerli bir durum ve en fazla 2000 karakterlik not gerekli." });
        var row = await db.QueryDiagnostics.FindAsync([id], ct);
        if (row is null) return Results.NotFound();
        row.ReviewStatus = request.Status;
        row.ReviewNote = QueryDiagnostics.Mask(request.Note);
        AddEvent(db, identity, "review", id, new { before = db.Entry(row).Property(x => x.ReviewStatus).OriginalValue, after = request.Status, row.ReviewNote });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Task<DiagnosticSettings?> FindSettings(DataAnalysisDbContext db, CancellationToken ct) => db.DiagnosticSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
    private static async Task<DiagnosticSettings> ReadSettings(DataAnalysisDbContext db, CancellationToken ct) => await FindSettings(db, ct) ?? new DiagnosticSettings();
    public static async Task<IResult> GetSettings(DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct)
        => !Allowed(identity) ? Results.Forbid() : Results.Ok(await ReadSettings(db, ct));
    public sealed record SettingsRequest(int RetentionDays, int RepeatedErrorThreshold, int SlowQuerySeconds);
    public static async Task<IResult> PutSettings(SettingsRequest request, DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct)
    {
        if (!Allowed(identity)) return Results.Forbid();
        if (request.RetentionDays is < 7 or > 365 || request.RepeatedErrorThreshold is < 2 or > 100 || request.SlowQuerySeconds is < 1 or > 3600)
            return Results.BadRequest(new { error = "Saklama: 7–365 gün; tekrar eşiği: 2–100; yavaşlık: 1–3600 saniye." });
        var row = await db.DiagnosticSettings.FindAsync([1], ct);
        if (row is null) { row = new DiagnosticSettings(); db.DiagnosticSettings.Add(row); }
        row.RetentionDays = request.RetentionDays; row.RepeatedErrorThreshold = request.RepeatedErrorThreshold; row.SlowQuerySeconds = request.SlowQuerySeconds;
        AddEvent(db, identity, "settings", null, request);
        await db.SaveChangesAsync(ct);
        return Results.Ok(row);
    }

    public static async Task<IResult> AdminEvents(DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct)
        => !Allowed(identity) ? Results.Forbid() : Results.Ok(await db.DiagnosticAdminEvents.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(100).ToListAsync(ct));
    private static void AddEvent(DataAnalysisDbContext db, IIdentityService identity, string action, Guid? id, object detail)
        => db.DiagnosticAdminEvents.Add(new DiagnosticAdminEvent { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
            Actor = identity.UserId.ToString(), Action = action, TargetId = id, DetailJson = QueryDiagnostics.SafeJson(JsonSerializer.Serialize(detail)) });

    public static async Task<IResult> Tests(DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct)
        => !Allowed(identity) ? Results.Forbid() : Results.Ok(await db.DiagnosticTestCases.AsNoTracking().OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.SourceId, t.Name, t.ExpectedJson, t.RunsJson, t.LastRunAt }).Take(200).ToListAsync(ct));
    public sealed record TestRequest(Guid SourceId, string Name, JsonElement Expected);
    public sealed record TestUpdateRequest(string Name, JsonElement Expected);
    private static bool ValidTest(string name, JsonElement expected) => !string.IsNullOrWhiteSpace(name) && name.Length <= 150
        && expected.ValueKind == JsonValueKind.Object && expected.EnumerateObject().Any() && expected.GetRawText().Length <= 16000;
    public static async Task<IResult> CreateTest(TestRequest request, DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct)
    {
        if (!Allowed(identity)) return Results.Forbid();
        if (!ValidTest(request.Name, request.Expected))
            return Results.BadRequest(new { error = "Test adı ve beklenen parametreleri içeren dolu bir JSON nesnesi gerekli." });
        var source = await db.QueryDiagnostics.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.SourceId, ct);
        if (source is null || QueryDiagnostics.Parse(source.ContextJson)?["dictionary"] is null)
            return Results.BadRequest(new { error = "Bu kaydın yeniden test için gerekli bağlamı yok." });
        var test = new DiagnosticTestCase { Id = Guid.NewGuid(), SourceId = source.Id, Name = QueryDiagnostics.Mask(request.Name),
            Question = source.Question, ContextJson = source.ContextJson, ExpectedJson = QueryDiagnostics.SafeJson(request.Expected.GetRawText()), CreatedAt = DateTime.UtcNow };
        db.DiagnosticTestCases.Add(test);
        AddEvent(db, identity, "test_created", test.Id, new { test.SourceId, test.Name });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { test.Id });
    }

    public static async Task<IResult> UpdateTest(Guid id, TestUpdateRequest request, DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct)
    {
        if (!Allowed(identity)) return Results.Forbid();
        if (!ValidTest(request.Name, request.Expected)) return Results.BadRequest(new { error = "Geçerli test adı ve beklenen parametreler gerekli." });
        var test = await db.DiagnosticTestCases.FindAsync([id], ct);
        if (test is null) return Results.NotFound();
        var before = test.ExpectedJson;
        test.Name = QueryDiagnostics.Mask(request.Name); test.ExpectedJson = QueryDiagnostics.SafeJson(request.Expected.GetRawText());
        AddEvent(db, identity, "test_updated", id, new { before, after = test.ExpectedJson, test.Name });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static async Task<IResult> DeleteTest(Guid id, DataAnalysisDbContext db, IIdentityService identity, CancellationToken ct)
    {
        if (!Allowed(identity)) return Results.Forbid();
        var test = await db.DiagnosticTestCases.FindAsync([id], ct);
        if (test is null) return Results.NotFound();
        db.Remove(test);
        AddEvent(db, identity, "test_deleted", id, new { test.Name, test.SourceId });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static async Task<IResult> RunTest(Guid id, DataAnalysisDbContext db, IIdentityService identity, LlmAnalysisService llm, CancellationToken ct)
    {
        if (!Allowed(identity)) return Results.Forbid();
        var test = await db.DiagnosticTestCases.FindAsync([id], ct);
        if (test is null) return Results.NotFound();
        var context = QueryDiagnostics.Parse(test.ContextJson)!;
        var result = await llm.TranslateQuestionAsync(test.Question, context["dictionary"]!.GetValue<string>(),
            context["schema"]?.GetValue<string>() ?? "", context["conversation"]?.GetValue<string>() ?? "", ct,
            referenceDate: context["date"]?.GetValue<DateTime>());
        var actual = QueryDiagnostics.Parse(result.Json);
        var differences = QueryDiagnostics.Differences(QueryDiagnostics.Parse(test.ExpectedJson), actual);
        string? validationError = null;
        if (result.Success)
            try
            {
                AgentQueryTableScope.Validate(result.Json, context["dictionary"]!.GetValue<string>(),
                    context["tables"]!.AsArray().Select(t => t!.GetValue<string>()).ToList());
            }
            catch (Exception e) when (e is QueryPolicyException or JsonException or InvalidOperationException)
            { validationError = QueryDiagnostics.Mask(e.Message); }
        var run = new JsonObject
        {
            ["at"] = DateTime.UtcNow, ["passed"] = result.Success && differences.Count == 0 && validationError is null,
            ["model"] = llm.ModelIdentity, ["promptVersion"] = QueryDiagnostics.PromptVersion,
            ["expected"] = QueryDiagnostics.Parse(test.ExpectedJson),
            ["differences"] = JsonSerializer.SerializeToNode(differences), ["actual"] = QueryDiagnostics.Parse(QueryDiagnostics.SafeJson(result.Json)),
            ["error"] = validationError ?? QueryDiagnostics.Mask(result.Error)
        };
        var runs = QueryDiagnostics.Parse(test.RunsJson) as JsonArray ?? [];
        runs.Insert(0, run); while (runs.Count > 20) runs.RemoveAt(runs.Count - 1);
        test.RunsJson = runs.ToJsonString(); test.LastRunAt = DateTime.UtcNow;
        AddEvent(db, identity, "test_run", id, new { passed = run["passed"]!.GetValue<bool>() });
        for (var attempt = 0; ; attempt++)
        {
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                await db.Entry(test).ReloadAsync(ct);
                if (db.Entry(test).State == EntityState.Detached) return Results.NotFound();
                var latest = QueryDiagnostics.Parse(test.RunsJson) as JsonArray ?? [];
                latest.Insert(0, run.DeepClone()); while (latest.Count > 20) latest.RemoveAt(latest.Count - 1);
                test.RunsJson = latest.ToJsonString(); test.LastRunAt = DateTime.UtcNow;
            }
            catch (DbUpdateConcurrencyException)
            { return Results.Conflict(new { error = "Kontrol sonucu eşzamanlı güncelleme nedeniyle kaydedilemedi. Yeniden deneyin." }); }
        }
        return Results.Ok(run.DeepClone());
    }
}

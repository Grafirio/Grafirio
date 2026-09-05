using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

/// <summary>
/// Baglantiyi sorgulanabilir hale getiren TEK adim: "Analiz Et".
///
/// Akis:
///   tablo secimi -> analiz baslat -> profil cikar (ornek degerlerle) ->
///   semantik sozluk -> (LLM emin degilse) sorular -> yanitlar -> ready
///
/// Onceden bu is ikiye bolunmustu: "On Analiz" profil cikarip semantik sozluk
/// uretiyor, "Analiz Et" ayrica ham semadan bir PyCaret config uretiyordu.
/// Kullanici ikisini de, dogru sirayla calistirmak zorundaydi; sorgu ucu da
/// ikisini birden zorunlu tutup yalnizca ikincisini okuyordu — yani on analizin
/// tum maliyeti odeniyor, urunu hicbir yerde kullanilmiyordu. Artik tek buton,
/// tek LLM cagrisi, tek cikti: sozluk hem analizin sonucu hem sorgunun girdisi.
/// </summary>
public static class AgentAnalyzeEndpoints
{
    public static void MapAgentAnalyzeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent")
            .RequireAuthorization("CompanyAccess")
            .WithTags("AI Agent")
            .WithOpenApi();

        // Analiz baglantinin semantik sozlugunu yazip kaydediyor; bu, veri
        // kaynagini degistirmek demek, yalnizca okumak degil.
        group.MapPost("/analyze-connection/{connectionId:guid}", AnalyzeConnection)
            .RequirePermission(AppPermissions.DataSourcesUpdate)
            .WithName("AnalyzeConnection")
            .WithDescription("Seçili tabloların profilini çıkarır ve semantik sözlük üretir");

        group.MapGet("/configs", ListConfigs)
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithName("ListAnalysisConfigs")
            .WithDescription("Firmanın analiz edilmiş bağlantılarını listeler");

        group.MapGet("/config/{connectionId:guid}", GetConfig)
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithName("GetAnalysisConfig")
            .WithDescription("Bağlantının semantik sözlüğünü getirir");

        group.MapGet("/config/{connectionId:guid}/status", GetConfigStatus)
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithName("GetConfigStatus")
            .WithDescription("Analiz durumunu ve varsa soruları döndürür");

        group.MapPost("/config/{connectionId:guid}/answers", SubmitAnswers)
            .RequirePermission(AppPermissions.DataSourcesUpdate)
            .WithName("SubmitAnalysisAnswers")
            .WithDescription("Kullanıcının soru yanıtlarını sözlüğe işler ve bağlantıyı hazır hale getirir");

        // "Ogrendiklerim". Bu iki ucun varligi pazarlik konusu degil: kalici
        // ve gorunmez bir bilgi, yanlis ogrenilmisse her sorguyu sessizce
        // bozar ve kullanicinin sebebi bulabilecegi hicbir yer olmaz.
        // Yanlis ogrenilmis bir bilgi, hic ogrenmemekten kotudur.
        group.MapPost("/config/{connectionId:guid}/learned", Learn)
            .RequirePermission(AppPermissions.DataSourcesUpdate)
            .WithName("LearnFact")
            .WithDescription("Kullanıcının onayladığı bir bilgiyi kalıcı olarak kaydeder");

        group.MapGet("/config/{connectionId:guid}/learned", ListLearned)
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithName("ListLearnedFacts")
            .WithDescription("Bu bağlantı için öğrenilmiş bilgileri listeler");

        group.MapDelete("/config/{connectionId:guid}/learned/{key}", ForgetLearned)
            .RequirePermission(AppPermissions.DataSourcesUpdate)
            .WithName("ForgetLearnedFact")
            .WithDescription("Öğrenilmiş tek bir bilgiyi siler");
    }

    /* ── Durumlar ─────────────────────────────────────────────────────────
       AnalysisConfig.Status tek gercek kaynak. Onceden durum hem Postgres'te
       (config) hem Mongo'da (profil) tutuluyordu ve ikisi birbirinden habersiz
       ilerliyordu. */
    public static class AnalysisStatus
    {
        public const string Analyzing = "analyzing";
        public const string AwaitingAnswers = "awaiting_answers";
        public const string Ready = "ready";
        public const string Failed = "failed";
    }

    private static async Task<IResult> AnalyzeConnection(
        Guid connectionId,
        AnalyzeRequest? request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ConnectionProfileStore profileStore,
        [FromServices] IPublishEndpoint publishEndpoint,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        var savedConn = await db.SavedConnections.FirstOrDefaultAsync(
            c => c.Id == connectionId && c.CompanyId == scopedCompanyId && c.IsActive, ct);

        if (savedConn is null)
            return Results.NotFound(new { error = "Bağlantı bulunamadı" });

        // Esnetilemez kural: tablo secimi olmadan analiz baslamaz.
        var selectedTables = await profileStore.GetSelectedTablesAsync(connectionId, scopedCompanyId, ct);
        if (selectedTables.Count == 0)
        {
            return Results.BadRequest(new
            {
                error = "Önce analiz edilecek tabloları seçin. Analiz yalnızca seçili tablolar üzerinde çalışır."
            });
        }

        // Ayni baglantinin eski config'i pasiflestiriliyor. Onceden mevcut bir
        // config varsa uc onu oldugu gibi geri donuyordu — yani "Analiz Et"
        // ikinci kez calistirilamiyor, tablo secimi degisse bile eski sozluk
        // kullanilmaya devam ediyordu.
        var previous = await db.AnalysisConfigs
            .Where(c => c.ConnectionId == connectionId && c.IsActive)
            .ToListAsync(ct);
        foreach (var old in previous) old.IsActive = false;

        var config = new AnalysisConfig
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            UserId = savedConn.UserId,
            CompanyId = savedConn.CompanyId,
            DatabaseName = savedConn.Database,
            TablesJson = JsonSerializer.Serialize(selectedTables),
            Status = AnalysisStatus.Analyzing,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        db.AnalysisConfigs.Add(config);
        await db.SaveChangesAsync(ct);

        // Is kuyruga birakiliyor ve uc hemen donuyor.
        //
        // Onceden her sey istek icinde yapiliyordu: profil + LLM cagrisi
        // birlikte gateway'in zaman asimini asiyor ve kullanici 504 goruyordu.
        // Sonuc aslinda uretilmis olabiliyordu ama istemci onu hic gormuyordu.
        // Bir sonraki denemede is `Task.Run`'a alindi; bu da yeterli degildi:
        // container yeniden baslarsa is kayboluyor ve kayit sonsuza kadar
        // "analyzing" kaliyordu. Kuyruk her ikisini de cozuyor.
        try
        {
            var currentTables = await profileStore.GetSelectedTablesAsync(connectionId, scopedCompanyId, ct);
            if (!AnalysisJobGuard.MatchesSelection(config.TablesJson, currentTables)
                || !await db.SavedConnections.AnyAsync(c => c.Id == connectionId
                    && c.CompanyId == scopedCompanyId && c.IsActive, ct)
                || !await db.AnalysisConfigs.AnyAsync(c => c.Id == config.Id && c.IsActive
                    && c.Status == AnalysisStatus.Analyzing, ct))
                throw new InvalidOperationException("Analysis selection or connection changed before publishing.");

            await publishEndpoint.Publish(new AnalyzeConnectionRequested
            {
                ConfigId = config.Id,
                ConnectionId = connectionId,
                CompanyId = scopedCompanyId,
                SamplingConsentGiven = request?.SamplingConsentGiven ?? false
            }, ct);
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger(nameof(AgentAnalyzeEndpoints))
                .LogError(exception, "Analysis publish failed for config {ConfigId}", config.Id);
            // Request cancellation must not prevent persisting the queue failure.
            await db.AnalysisConfigs.Where(c => c.Id == config.Id && c.IsActive && c.Status == AnalysisStatus.Analyzing)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(c => c.Status, AnalysisStatus.Failed)
                    .SetProperty(c => c.SchemaSummary, "Analysis could not be queued. Please retry.")
                    .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), CancellationToken.None);
            return Results.Problem("Analiz kuyruğa alınamadı. Yeniden deneyin.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // 202: is kabul edildi, sonuc icin durumu sorgula.
        return Results.Accepted(value: new
        {
            success = true,
            configId = config.Id,
            status = AnalysisStatus.Analyzing,
            tableCount = selectedTables.Count,
            message = "Analiz başlatıldı. Tablolar okunuyor ve anlamlandırılıyor."
        });
    }

    private static async Task<IResult> GetConfigStatus(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var config = await ActiveConfig(db, connectionId, companyId.Value.ToString(), ct);

        if (config is null)
            return Results.Ok(new { status = "none", questions = Array.Empty<object>() });

        var stats = ExtractProfileStats(config.ConfigJson);

        return Results.Ok(new
        {
            status = config.Status,
            configId = config.Id,
            questions = config.Status == AnalysisStatus.AwaitingAnswers ? ExtractQuestions(config.ConfigJson) : [],
            questionCount = config.Status == AnalysisStatus.AwaitingAnswers ? AnalysisAnswers.CountPending(config.ConfigJson) : 0,
            summary = config.SchemaSummary,
            tableCount = stats?.TableCount,
            columnCount = stats?.ColumnCount,
            sampledColumnCount = stats?.SampledColumnCount,
            updatedAt = config.UpdatedAt ?? config.CreatedAt
        });
    }

    /// <summary>
    /// Firmanin analiz edilmis baglantilari. Panel bu listeyi onceden
    /// tarayicinin localStorage'indan okuyordu: baska bir makineden girildiginde
    /// ya da gecmis temizlendiginde "hic analiz yok" gorunuyordu, ustelik liste
    /// basarisiz analizleri de "hazir" diye tutabiliyordu.
    /// </summary>
    private static async Task<IResult> ListConfigs(
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        var configs = await db.AnalysisConfigs
            .Where(c => c.CompanyId == scopedCompanyId && c.IsActive)
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Select(c => new
            {
                connectionId = c.ConnectionId,
                configId = c.Id,
                database = c.DatabaseName,
                status = c.Status,
                summary = c.SchemaSummary,
                tablesJson = c.TablesJson,
                createdAt = c.CreatedAt,
                updatedAt = c.UpdatedAt
            })
            .ToListAsync(ct);

        // Tablo listesi metin olarak saklaniyor; sayiya cevirmek istemcinin
        // isi olmasin diye burada acilliyor.
        var analyses = configs.Select(c => new
        {
            c.connectionId,
            c.configId,
            c.database,
            c.status,
            c.summary,
            tableCount = SafeParse(c.tablesJson)?.ValueKind == JsonValueKind.Array
                ? SafeParse(c.tablesJson)!.Value.GetArrayLength()
                : 0,
            c.createdAt,
            c.updatedAt
        });

        return Results.Ok(new { analyses });
    }

    private static async Task<IResult> GetConfig(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var config = await ActiveConfig(db, connectionId, companyId.Value.ToString(), ct);

        if (config is null)
            return Results.NotFound(new { error = "Bu bağlantı için analiz yapılmamış" });

        return Results.Ok(new
        {
            configId = config.Id,
            status = config.Status,
            database = config.DatabaseName,
            dictionary = SafeParse(config.ConfigJson),
            summary = config.SchemaSummary,
            tables = SafeParse(config.TablesJson),
            createdAt = config.CreatedAt,
            updatedAt = config.UpdatedAt
        });
    }

    /// <summary>
    /// Kullanicinin yanitlarini sozluge kalici olarak isler: yanitlanan kolonun
    /// <c>meaning</c> alani yanit olur, <c>confidence</c> "high"a cikar ve soru
    /// listeden dusulur. Amac, ayni seyin bir daha sorulmamasi ve sorgu aninda
    /// modelin dogru tanimi gormesi — yanitlari ayri bir yerde biriktirmek bu
    /// ikisini de saglamiyordu.
    /// </summary>
    private static async Task<IResult> SubmitAnswers(
        Guid connectionId,
        AnswersRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] LearnedFactStore facts,
        [FromServices] ConnectionProfileStore profileStore,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var config = await ActiveConfig(db, connectionId, companyId.Value.ToString(), ct);

        if (config is null)
            return Results.BadRequest(new { error = "Önce 'Analiz Et' çalıştırın." });

        if (config.Status != AnalysisStatus.AwaitingAnswers)
            return Results.Conflict(new { error = "Bu analiz yanıt beklemiyor." });
        var scopedCompanyId = companyId.Value.ToString();
        if (!await db.SavedConnections.AnyAsync(connection => connection.Id == connectionId
            && connection.CompanyId == scopedCompanyId && connection.IsActive, ct)) return Results.NotFound();
        if (!AnalysisJobGuard.MatchesSelection(config.TablesJson,
            await profileStore.GetSelectedTablesAsync(connectionId, scopedCompanyId, ct)))
            return Results.Conflict(new { error = "Tablo seçimi değişti. Yeniden analiz edin." });

        string updatedDictionary;
        List<LearnedFact> learned;
        int remainingCount;
        try
        {
            updatedDictionary = AnalysisAnswers.Apply(config.ConfigJson, request.Answers, out learned, out remainingCount);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = "Yanıtlar veya sözlük geçersiz. Analiz durumunu yenileyin." });
        }

        var status = remainingCount == 0 ? AnalysisStatus.Ready : AnalysisStatus.AwaitingAnswers;
        var updated = await db.AnalysisConfigs.Where(current => current.Id == config.Id && current.IsActive
                && current.CompanyId == scopedCompanyId && current.ConnectionId == connectionId
                && current.Status == AnalysisStatus.AwaitingAnswers && current.ConfigJson == config.ConfigJson
                && current.TablesJson == config.TablesJson
                && db.SavedConnections.Any(connection => connection.Id == connectionId
                    && connection.CompanyId == scopedCompanyId && connection.IsActive))
            .ExecuteUpdateAsync(update => update.SetProperty(current => current.ConfigJson, updatedDictionary)
                .SetProperty(current => current.Status, status)
                .SetProperty(current => current.UpdatedAt, DateTime.UtcNow), ct);
        if (updated != 1) return Results.Conflict(new { error = "Analiz değişti. Durumu yenileyin." });

        // Sozluge yazmak bu turu kurtariyor, kalici depo bir sonrakini.
        // Ikisi birden yaziliyor: kullanici cevabinin bu analizde de gecerli
        // olmasini bekliyor, yeniden analiz beklemesini istemiyoruz.
        foreach (var fact in learned)
            await facts.SaveAsync(connectionId, companyId.Value.ToString(), fact, ct);

        return Results.Ok(new { success = true, status, learned = learned.Count, questionCount = remainingCount });
    }

    private static Task<AnalysisConfig?> ActiveConfig(
        DataAnalysisDbContext db, Guid connectionId, string companyId, CancellationToken ct) =>
        db.AnalysisConfigs
            .Where(c => c.ConnectionId == connectionId && c.CompanyId == companyId && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /* ── Sozluk uzerinde islemler ─────────────────────────────────────── */

    /* ── "Ogrendiklerim" ──────────────────────────────────────────────── */

    /// <summary>
    /// Applies validated relationship decisions immediately; other facts are used during reanalysis.
    /// </summary>
    private static async Task<IResult> Learn(
        Guid connectionId,
        LearnRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] LearnedFactStore facts,
        [FromServices] IRelationshipApprovalService relationships,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var fact = BuildFact(request, identity.UserId.ToString());
        if (fact is null)
            return Results.BadRequest(new
            {
                error = "Eksik ya da tanınmayan bilgi türü. Beklenen: "
                      + string.Join(", ", LearnedFact.AllKinds)
            });

        if (fact.Kind == LearnedFact.Relationship)
        {
            var result = await relationships.ApplyAsync(connectionId, companyId.Value.ToString(), fact, ct);
            if (!result.Applied)
                return Results.Json(new { error = result.Error, applied = false }, statusCode: result.StatusCode);
            return Results.Ok(new { success = true, applied = true, key = fact.Key, description = fact.Describe() });
        }

        var scopedCompanyId = companyId.Value.ToString();
        if (!await db.SavedConnections.AnyAsync(connection => connection.Id == connectionId
            && connection.CompanyId == scopedCompanyId && connection.IsActive, ct))
            return Results.NotFound(new { error = "Bağlantı bulunamadı." });

        await facts.SaveAsync(connectionId, scopedCompanyId, fact, ct);
        return Results.Ok(new { success = true, applied = false, key = fact.Key, description = fact.Describe() });
    }

    /// <summary>
    /// Istegi bir kayda cevirir. Eksik alanli istek <c>null</c> doner —
    /// yarim bir kaydi yazmak, hicbir zaman eslesmeyecek bir anahtari
    /// kalici hale getirmek olurdu.
    /// </summary>
    private static LearnedFact? BuildFact(LearnRequest request, string? userId)
    {
        static bool Has(string? value) => !string.IsNullOrWhiteSpace(value);

        return request.Kind switch
        {
            LearnedFact.Relationship
                when Has(request.FromTable) && Has(request.FromColumn)
                  && Has(request.ToTable) && Has(request.ToColumn) =>
                LearnedFact.ForRelationship(
                    request.FromTable!, request.FromColumn!, request.ToTable!, request.ToColumn!,
                    request.Accepted, request.Question, userId),

            LearnedFact.Synonym when Has(request.Table) && Has(request.Means) =>
                LearnedFact.ForSynonym(
                    request.Table!, request.Column, request.Means!,
                    request.Accepted, request.Question, userId),

            LearnedFact.Meaning when Has(request.Table) && Has(request.Means) =>
                LearnedFact.ForMeaning(
                    request.Table!, request.Column, request.Means!,
                    request.Accepted, request.Question, userId),

            LearnedFact.CodeMeaning
                when Has(request.Table) && Has(request.Column)
                  && Has(request.Value) && Has(request.Means) =>
                LearnedFact.ForCodeMeaning(
                    request.Table!, request.Column!, request.Value!, request.Means!,
                    request.Accepted, request.Question, userId),

            LearnedFact.Label when Has(request.Table) && Has(request.Column) =>
                LearnedFact.ForLabel(
                    request.Table!, request.Column!,
                    request.Accepted, request.Question, userId),

            _ => null,
        };
    }

    /// <summary>
    /// Bu baglanti icin ogrenilmis her sey — reddedilenler dahil.
    ///
    /// Reddedilenler de gosteriliyor cunku onlar da bir karar: kullanici
    /// "bu eslesme yanlis" dedigi icin sistem o baglantiyi bir daha kurmuyor.
    /// Fikri degistiyse gorup silebilmeli, yoksa o kapi sonsuza kadar kapali
    /// kalir ve neden kapali oldugu hicbir yerde yazmaz.
    /// </summary>
    private static async Task<IResult> ListLearned(
        Guid connectionId,
        [FromServices] LearnedFactStore facts,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var all = await facts.GetAllAsync(connectionId, companyId.Value.ToString(), ct);

        return Results.Ok(new
        {
            count = all.Count,
            items = all.Select(f => new
            {
                key = f.Key,
                kind = f.Kind,
                accepted = f.Accepted,
                // Ekranda gosterilecek olan bu: teknik kimlik degil, cumle.
                description = f.Describe(),
                question = f.Question,
                // Son analizde kurulamadıysa sebebi. Kullanıcı bunu görmeden
                // bağlantısının çalıştığını sanar.
                problem = f.Problem,
                createdAt = f.CreatedAt
            })
        });
    }

    private static async Task<IResult> ForgetLearned(
        Guid connectionId,
        string key,
        [FromServices] IRelationshipApprovalService relationships,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var result = await relationships.ForgetAsync(connectionId, companyId.Value.ToString(), key, ct);

        return result.Applied
            ? Results.Ok(new { success = true })
            : Results.Json(new { error = result.Error, applied = false }, statusCode: result.StatusCode);
    }

    private static ProfileStats? ExtractProfileStats(string dictionaryJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dictionaryJson);
            if (!doc.RootElement.TryGetProperty("profileStats", out var stats)) return null;

            return new ProfileStats(
                stats.TryGetProperty("tableCount", out var t) ? t.GetInt32() : 0,
                stats.TryGetProperty("columnCount", out var c) ? c.GetInt32() : 0,
                stats.TryGetProperty("sampledColumnCount", out var s) ? s.GetInt32() : 0);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static List<QuestionDto> ExtractQuestions(string? dictionaryJson)
    {
        var root = AnalysisAnswers.Read(dictionaryJson ?? "");
        return CanonicalSchemaDictionary.Objects(root, "questions")
            .Select(question => new QuestionDto(
                CanonicalSchemaDictionary.Text(question, "id"),
                CanonicalSchemaDictionary.Text(question, "table"),
                CanonicalSchemaDictionary.OptionalText(question, "column"),
                CanonicalSchemaDictionary.Text(question, "question"),
                (question["options"] as JsonArray)?.Select(option => option!.GetValue<string>()).ToList() ?? []))
            .ToList();
    }

    private static JsonElement? SafeParse(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return null; }
    }

}

public record AnalyzeRequest(bool SamplingConsentGiven);

public record AnswersRequest(Dictionary<string, string>? Answers);

/// <summary>
/// Kullanicinin onayladigi (ya da reddettigi) tek bir bilgi.
///
/// <paramref name="Accepted"/> <c>false</c> gelebilir ve bu bir hata degil,
/// bilgidir: "bu eslesme yanlis" cevabi da kaydediliyor. Yoksa sistem ayni
/// yanlis eslesmeyi her sorguda yeniden kurar ve yeniden sorar.
/// </summary>
public record LearnRequest(
    string Kind,
    bool Accepted,
    string? FromTable,
    string? FromColumn,
    string? ToTable,
    string? ToColumn,
    string? Table,
    string? Column,
    string? Value,
    string? Means,
    string? Question);

public record QuestionDto(string Id, string? Table, string? Column, string Question, List<string> Options);

internal record ProfileStats(int TableCount, int ColumnCount, int SampledColumnCount);

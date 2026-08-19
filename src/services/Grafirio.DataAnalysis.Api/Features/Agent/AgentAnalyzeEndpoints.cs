using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// <summary>
    /// Kurulumda sorulacak en fazla soru sayisi. Ilk surumde model bir kolona
    /// bagli olmayan, "raporda neyi gormek istersiniz" turunden uzun tercih
    /// sorulari uretiyordu; kullanici sorularin ne dedigini anlayamiyordu.
    /// Ust sinir ve kolon sarti, bunun tekrarlamamasi icin.
    /// </summary>
    private const int MaxQuestions = 8;

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
            .WithName("SubmitAnalysisAnswers")
            .WithDescription("Kullanıcının soru yanıtlarını sözlüğe işler ve bağlantıyı hazır hale getirir");
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
        await publishEndpoint.Publish(new AnalyzeConnectionRequested
        {
            ConfigId = config.Id,
            ConnectionId = connectionId,
            CompanyId = scopedCompanyId,
            SamplingConsentGiven = request?.SamplingConsentGiven ?? false
        }, ct);

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
            questions = ExtractQuestions(config.ConfigJson),
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
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var config = await ActiveConfig(db, connectionId, companyId.Value.ToString(), ct);

        if (config is null)
            return Results.BadRequest(new { error = "Önce 'Analiz Et' çalıştırın." });

        config.ConfigJson = ApplyAnswers(config.ConfigJson, request.Answers ?? []);
        config.Status = AnalysisStatus.Ready;
        config.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { success = true, status = config.Status });
    }

    private static Task<AnalysisConfig?> ActiveConfig(
        DataAnalysisDbContext db, Guid connectionId, string companyId, CancellationToken ct) =>
        db.AnalysisConfigs
            .Where(c => c.ConnectionId == connectionId && c.CompanyId == companyId && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /* ── Sozluk uzerinde islemler ─────────────────────────────────────── */

    private static string ApplyAnswers(string dictionaryJson, Dictionary<string, string> answers)
    {
        if (answers.Count == 0) return dictionaryJson;

        try
        {
            if (JsonNode.Parse(dictionaryJson) is not JsonObject root) return dictionaryJson;

            var questions = root["questions"] as JsonArray;
            if (questions is null) return dictionaryJson;

            var columns = root["columns"] as JsonArray;
            if (columns is null)
            {
                columns = [];
                root["columns"] = columns;
            }

            var remaining = new JsonArray();

            foreach (var node in questions)
            {
                if (node is not JsonObject question) continue;

                var id = question["id"]?.GetValue<string>();
                if (id is null || !answers.TryGetValue(id, out var answer) || string.IsNullOrWhiteSpace(answer))
                {
                    // Yanitlanmayan soru duruyor; kullanici sonra tamamlayabilir.
                    remaining.Add(question.DeepClone());
                    continue;
                }

                var table = question["table"]?.GetValue<string>();
                var column = question["column"]?.GetValue<string>();
                if (column is null) continue;

                var existing = columns.OfType<JsonObject>().FirstOrDefault(c =>
                    string.Equals(c["column"]?.GetValue<string>(), column, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c["table"]?.GetValue<string>(), table, StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                {
                    columns.Add(new JsonObject
                    {
                        ["table"] = table,
                        ["column"] = column,
                        ["meaning"] = answer,
                        ["role"] = "other",
                        ["confidence"] = "high",
                        ["source"] = "user"
                    });
                }
                else
                {
                    existing["meaning"] = answer;
                    existing["confidence"] = "high";
                    existing["source"] = "user";
                }
            }

            root["questions"] = remaining;
            return root.ToJsonString(JsonOptions);
        }
        catch (JsonException)
        {
            // Bozuk sozluk yanit kaydini engellemesin; durum yine ready olur.
            return dictionaryJson;
        }
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
        if (string.IsNullOrWhiteSpace(dictionaryJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(dictionaryJson);
            if (!doc.RootElement.TryGetProperty("questions", out var questions)
                || questions.ValueKind != JsonValueKind.Array)
                return [];

            return questions.EnumerateArray()
                .Select(q => new QuestionDto(
                    ReadString(q, "id") ?? Guid.NewGuid().ToString("N")[..8],
                    ReadString(q, "table"),
                    ReadString(q, "column"),
                    ReadString(q, "question") ?? "",
                    q.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                        ? o.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
                        : []))
                .Where(q => q.Question.Length > 0)
                // Bir kolona bagli olmayan soru, kolon anlamini sormuyor
                // demektir — genellikle "raporda neyi gormek istersiniz"
                // turunden bir tercih sorusu. Onlar sorgu anininin isi.
                .Where(q => !string.IsNullOrWhiteSpace(q.Column))
                // Ayni kolon icin birden fazla soru sorulmasin.
                .GroupBy(q => $"{q.Table}.{q.Column}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                // Kurulum adimi bir ankete donusmesin.
                .Take(MaxQuestions)
                .ToList();
        }
        catch (JsonException)
        {
            // Model bozuk JSON dondurduyse sorular kaybolur ama akis durmaz;
            // durum yine de kaydedilmis olur.
            return [];
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? SafeParse(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return null; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

public record AnalyzeRequest(bool SamplingConsentGiven);

public record AnswersRequest(Dictionary<string, string>? Answers);

public record QuestionDto(string Id, string? Table, string? Column, string Question, List<string> Options);

internal record ProfileStats(int TableCount, int ColumnCount, int SampledColumnCount);

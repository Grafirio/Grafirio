using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>
/// On analiz: baglanti kurulurken veritabanini anlayan bekletici kapi.
///
/// Akis:
///   tablo secimi -> on analiz baslat -> profil cikar -> semantik sozluk ->
///   (LLM emin degilse) sorular -> yanitlar -> ready
///
/// `ready` olmayan baglanti dashboard'da grafik uretemez. Amac, kullanicinin
/// kolon adi bilmek zorunda kalmamasi: sistem bir kez ogreniyor, emin
/// olamadigini bir kez soruyor, sonrasinda her soruda dogru kolonu buluyor.
/// </summary>
public static class PreAnalysisEndpoints
{
    public static void MapPreAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connections/{connectionId:guid}/pre-analysis")
            // Politika adli: paylasilan kurulumda varsayilan sema yok.
            .RequireAuthorization("CompanyAccess")
            .WithTags("Pre-Analysis")
            .WithOpenApi();

        group.MapPost("/", Start)
            .WithName("StartPreAnalysis")
            .WithDescription("Seçili tabloların profilini çıkarır ve semantik sözlük üretir");

        group.MapGet("/", GetState)
            .WithName("GetPreAnalysisState")
            .WithDescription("Ön analiz durumunu ve varsa soruları döndürür");

        group.MapPost("/answers", SubmitAnswers)
            .WithName("SubmitPreAnalysisAnswers")
            .WithDescription("Kullanıcının soru yanıtlarını sözlüğe işler ve bağlantıyı hazır hale getirir");
    }

    private static async Task<IResult> Start(
        Guid connectionId,
        PreAnalysisRequest? request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ConnectionProfileStore store,
        [FromServices] SchemaProfiler profiler,
        [FromServices] GeminiService llm,
        [FromServices] IIdentityService identity,
        [FromServices] ILogger<SchemaProfiler> logger,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        var connection = await db.SavedConnections.FirstOrDefaultAsync(
            c => c.Id == connectionId && c.CompanyId == scopedCompanyId && c.IsActive, ct);

        if (connection is null) return Results.NotFound(new { error = "Bağlantı bulunamadı" });

        // Esnetilemez kural: tablo secimi olmadan on analiz baslamaz.
        var selectedTables = await store.GetSelectedTablesAsync(connectionId, scopedCompanyId, ct);
        if (selectedTables.Count == 0)
        {
            return Results.BadRequest(new
            {
                error = "Önce analiz edilecek tabloları seçin. Ön analiz yalnızca seçili tablolar üzerinde çalışır."
            });
        }

        await store.SetStatusAsync(connectionId, scopedCompanyId, ProfileStatus.Profiling, ct: ct);

        DatabaseProfile profile;
        try
        {
            var password = EncryptionHelper.Decrypt(connection.EncryptedPassword);
            var connectionString = BuildConnectionString(connection, password);

            profile = await profiler.ProfileAsync(
                connectionString, connection.Database, selectedTables,
                request?.SamplingConsentGiven ?? false, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Profil çıkarılamadı. Connection: {ConnectionId}", connectionId);
            await store.SetStatusAsync(connectionId, scopedCompanyId, ProfileStatus.Failed, ex.Message, ct);
            return Results.Problem($"Veritabanı profili çıkarılamadı: {ex.Message}");
        }

        var profileJson = JsonSerializer.Serialize(profile, JsonOptions);
        var dictionaryResult = await llm.BuildSemanticDictionary(profileJson, ct);

        if (!dictionaryResult.Success)
        {
            await store.SetStatusAsync(connectionId, scopedCompanyId, ProfileStatus.Failed, dictionaryResult.Error, ct);
            return dictionaryResult.IsConfigurationError
                ? Results.Problem(detail: dictionaryResult.Error,
                    title: "Yapay zekâ servisi yapılandırılmamış",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Problem(detail: dictionaryResult.Error,
                    title: "Semantik sözlük üretilemedi",
                    statusCode: StatusCodes.Status502BadGateway);
        }

        var questions = ExtractQuestions(dictionaryResult.PyCaretParamsJson);
        var status = questions.Count > 0 ? ProfileStatus.AwaitingAnswers : ProfileStatus.Ready;

        var stored = JsonSerializer.Serialize(new StoredProfile
        {
            Profile = profileJson,
            Dictionary = dictionaryResult.PyCaretParamsJson,
            Summary = dictionaryResult.Explanation
        }, JsonOptions);

        await store.SaveProfileAsync(connectionId, scopedCompanyId, stored, status, ct);

        return Results.Ok(new
        {
            success = true,
            status,
            tableCount = profile.Tables.Count,
            columnCount = profile.Tables.Sum(t => t.Columns.Count),
            sampledColumnCount = profile.Tables.Sum(t => t.Columns.Count(c => c.SampleValues.Count > 0)),
            questions,
            summary = dictionaryResult.Explanation
        });
    }

    private static async Task<IResult> GetState(
        Guid connectionId,
        [FromServices] ConnectionProfileStore store,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        var (json, status) = await store.GetProfileAsync(connectionId, scopedCompanyId, ct);
        var tables = await store.GetSelectedTablesAsync(connectionId, scopedCompanyId, ct);

        if (json is null)
            return Results.Ok(new { status, selectedTables = tables, questions = Array.Empty<object>() });

        var stored = JsonSerializer.Deserialize<StoredProfile>(json, JsonOptions);
        return Results.Ok(new
        {
            status,
            selectedTables = tables,
            questions = ExtractQuestions(stored?.Dictionary),
            summary = stored?.Summary,
            answers = stored?.Answers
        });
    }

    private static async Task<IResult> SubmitAnswers(
        Guid connectionId,
        AnswersRequest request,
        [FromServices] ConnectionProfileStore store,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        var (json, _) = await store.GetProfileAsync(connectionId, scopedCompanyId, ct);
        if (json is null)
            return Results.BadRequest(new { error = "Önce ön analizi çalıştırın." });

        var stored = JsonSerializer.Deserialize<StoredProfile>(json, JsonOptions) ?? new StoredProfile();

        // Yanitlar sozluge kalici olarak isleniyor: bir kez soruluyor,
        // her soruda degil.
        stored.Answers = request.Answers ?? [];

        await store.SaveProfileAsync(
            connectionId, scopedCompanyId,
            JsonSerializer.Serialize(stored, JsonOptions),
            ProfileStatus.Ready, ct);

        return Results.Ok(new { success = true, status = ProfileStatus.Ready });
    }

    private static List<QuestionDto> ExtractQuestions(string? dictionaryJson)
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
                    Read(q, "id") ?? Guid.NewGuid().ToString("N")[..8],
                    Read(q, "table"),
                    Read(q, "column"),
                    Read(q, "question") ?? "",
                    q.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                        ? o.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
                        : []))
                .Where(q => q.Question.Length > 0)
                .ToList();
        }
        catch (JsonException)
        {
            // Model bozuk JSON dondurduyse sorular kaybolur ama akis durmaz;
            // durum yine de kaydedilmis olur.
            return [];
        }
    }

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string BuildConnectionString(Data.Entities.SavedConnection c, string password) =>
        $"Server={c.Host},{c.Port};Database={c.Database};User Id={c.Username};Password={password};" +
        $"TrustServerCertificate={(c.TrustServerCertificate ? "True" : "False")};Encrypt=True;Connection Timeout=30";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

public record PreAnalysisRequest(bool SamplingConsentGiven);

public record AnswersRequest(Dictionary<string, string>? Answers);

public record QuestionDto(string Id, string? Table, string? Column, string Question, List<string> Options);

public class StoredProfile
{
    public string Profile { get; set; } = "";
    public string Dictionary { get; set; } = "";
    public string Summary { get; set; } = "";
    public Dictionary<string, string> Answers { get; set; } = [];
}

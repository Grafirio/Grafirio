using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

public static class AgentQueryEndpoints
{
    public static void MapAgentQueryEndpoints(this IEndpointRouteBuilder app)
    {
        // Kimlik dogrulamasi zorunlu: uc artik sirket suzgeci uyguluyor ve
        // bunu token'daki company_id'den aliyor. Yetkilendirmesiz birakilirsa
        // kimligi olmayan cagri sessizce Forbid'e dusuyor; niyeti acikca
        // belirtmek daha dogru.
        var group = app.MapGroup("/api/agent")
            .RequireAuthorization("CompanyAccess")
            .WithTags("AI Agent Query")
            .WithOpenApi();

        group.MapPost("/query", SubmitQuery)
            .WithName("SubmitAgentQuery")
            .WithDescription("Kullanıcı sorusunu LLM ile analiz edip PyCaret'e gönderir");

        group.MapGet("/query/{queryId:guid}/status", GetQueryStatus)
            .WithName("GetQueryStatus")
            .WithDescription("Sorgu işlem durumunu kontrol eder");

        group.MapGet("/query/{queryId:guid}/result", GetQueryResult)
            .WithName("GetQueryResult")
            .WithDescription("Sorgu sonucunu getirir");

        group.MapGet("/queries/{connectionId:guid}", GetQueryHistory)
            .WithName("GetQueryHistory")
            .WithDescription("Bağlantıya ait sorgu geçmişini getirir");
    }

    public record QueryRequest(Guid ConnectionId, string Question);

    private static async Task<IResult> SubmitQuery(
        [FromBody] QueryRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] GeminiService gemini,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IConfiguration configuration,
        [FromServices] ConnectionProfileStore profileStore,
        [FromServices] IIdentityService identity,
        [FromServices] ILogger<GeminiService> logger)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        // Bekletici kapi: on analiz tamamlanmadan sorgu calistirilmaz.
        // Sema profili ve semantik sozluk olmadan LLM kolon adlarini tahmin
        // etmek zorunda kaliyor ve olmayan kolonlar uyduruyordu.
        var (_, profileStatus) = await profileStore.GetProfileAsync(
            request.ConnectionId, scopedCompanyId);

        if (profileStatus != ProfileStatus.Ready)
        {
            return Results.BadRequest(new
            {
                error = profileStatus switch
                {
                    ProfileStatus.AwaitingAnswers =>
                        "Ön analiz soruları yanıtlanmayı bekliyor. SQL Bağlantı Ayarları'ndan tamamlayın.",
                    ProfileStatus.Profiling =>
                        "Ön analiz sürüyor. Tamamlanınca sorgu gönderebilirsiniz.",
                    ProfileStatus.Failed =>
                        "Ön analiz başarısız oldu. SQL Bağlantı Ayarları'ndan tekrar çalıştırın.",
                    _ =>
                        "Bu bağlantı için ön analiz yapılmamış. Önce tabloları seçip 'Ön Analiz' çalıştırın."
                },
                status = profileStatus
            });
        }

        // 1. Config'i bul
        var config = await db.AnalysisConfigs
            .FirstOrDefaultAsync(c => c.ConnectionId == request.ConnectionId && c.IsActive && c.Status == "ready");

        if (config is null)
            return Results.BadRequest(new { error = "Bu bağlantı için henüz analiz yapılmamış. Önce 'Analiz Et' butonuna tıklayın." });

        // 2. Bağlantı bilgilerini al — sirket suzgeciyle: baska bir sirketin
        // baglanti kimligini bilen biri onun veritabanini sorgulayamasin.
        var savedConn = await db.SavedConnections
            .FirstOrDefaultAsync(c => c.Id == request.ConnectionId
                                   && c.CompanyId == scopedCompanyId
                                   && c.IsActive);

        if (savedConn is null)
            return Results.NotFound(new { error = "Bağlantı bulunamadı" });

        // 3. Gemini'ye soruyu gönder → PyCaret parametreleri
        var geminiResult = await gemini.TranslateQueryForPyCaret(
            request.Question, config.ConfigJson, config.SchemaSummary);

        // Eksik yapilandirma bir cokme degil; 500 yerine 503 donuluyor ki
        // arayuz "sunucu hatasi" yerine sebebi gosterebilsin.
        if (!geminiResult.Success)
        {
            return geminiResult.IsConfigurationError
                ? Results.Problem(
                    detail: geminiResult.Error,
                    title: "Yapay zekâ servisi yapılandırılmamış",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Problem(
                    detail: geminiResult.Error,
                    title: "Soru analizi başarısız",
                    statusCode: StatusCodes.Status502BadGateway);
        }

        // 4. QueryHistory kaydet
        var queryHistory = new QueryHistory
        {
            Id = Guid.NewGuid(),
            ConfigId = config.Id,
            UserId = config.UserId,
            Question = request.Question,
            PyCaretParamsJson = geminiResult.PyCaretParamsJson,
            Status = "processing",
            CreatedAt = DateTime.UtcNow
        };

        db.QueryHistories.Add(queryHistory);
        await db.SaveChangesAsync();

        // 5. PyCaret Engine'e HTTP ile gönder
        try
        {
            var password = EncryptionHelper.Decrypt(savedConn.EncryptedPassword);

            var pycaretRequest = new
            {
                request_id = Guid.NewGuid().ToString(),
                query_id = queryHistory.Id.ToString(),
                company_id = savedConn.CompanyId,
                db_host = savedConn.Host,
                db_port = savedConn.Port,
                db_name = savedConn.Database,
                db_user = savedConn.Username,
                db_password = password,
                config_json = config.ConfigJson,
                analysis_params_json = geminiResult.PyCaretParamsJson,
                user_question = request.Question
            };

            var pycaretUrl = configuration["PyCaret:BaseUrl"] ?? "http://pycaret-engine:8002";
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            var response = await client.PostAsJsonAsync($"{pycaretUrl}/agent/analyze", pycaretRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                logger.LogError("PyCaret Engine hata döndü: {StatusCode} - {Body}", response.StatusCode, errorBody);

                queryHistory.Status = "failed";
                queryHistory.ResultJson = JsonSerializer.Serialize(new { error = $"PyCaret hatası: {response.StatusCode}" });
                await db.SaveChangesAsync();

                return Results.Problem($"PyCaret Engine hatası: {response.StatusCode}");
            }

            logger.LogInformation("Sorgu PyCaret'e gönderildi: {QueryId}", queryHistory.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PyCaret Engine'e bağlanılamadı");
            queryHistory.Status = "failed";
            queryHistory.ResultJson = JsonSerializer.Serialize(new { error = ex.Message });
            await db.SaveChangesAsync();

            return Results.Problem($"PyCaret Engine'e bağlanılamadı: {ex.Message}");
        }

        return Results.Ok(new
        {
            success = true,
            queryId = queryHistory.Id,
            status = "processing",
            explanation = geminiResult.Explanation,
            message = "Sorgunuz analiz edilmeye başlandı"
        });
    }

    private static async Task<IResult> GetQueryStatus(
        Guid queryId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<GeminiService> logger)
    {
        var query = await db.QueryHistories.FindAsync(queryId);
        if (query is null)
            return Results.NotFound(new { error = "Sorgu bulunamadı" });

        // Eğer processing ise PyCaret'ten durumu kontrol et
        if (query.Status == "processing")
        {
            try
            {
                var pycaretUrl = configuration["PyCaret:BaseUrl"] ?? "http://pycaret-engine:8002";
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var response = await client.GetAsync($"{pycaretUrl}/agent/analyze/status/{queryId}");

                if (response.IsSuccessStatusCode)
                {
                    var statusResult = await response.Content.ReadFromJsonAsync<JsonElement>();

                    if (statusResult.TryGetProperty("status", out var statusProp))
                    {
                        var pyStatus = statusProp.GetString();

                        if (pyStatus == "completed")
                        {
                            // Sonucu al
                            var resultResponse = await client.GetAsync($"{pycaretUrl}/agent/analyze/result/{queryId}");
                            if (resultResponse.IsSuccessStatusCode)
                            {
                                var resultJson = await resultResponse.Content.ReadAsStringAsync();
                                query.Status = "completed";
                                query.ResultJson = resultJson;
                                query.CompletedAt = DateTime.UtcNow;
                                await db.SaveChangesAsync();
                            }
                        }
                        else if (pyStatus == "failed")
                        {
                            var msg = statusResult.TryGetProperty("message", out var msgProp)
                                ? msgProp.GetString() : "Analiz başarısız";
                            query.Status = "failed";
                            query.ResultJson = JsonSerializer.Serialize(new { error = msg });
                            await db.SaveChangesAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PyCaret status kontrolü başarısız");
            }
        }

        // Basarisiz sorgunun sebebi ResultJson'a yaziliyordu ama cevapta hic
        // yer almiyordu; arayuz de mecburen "Analiz basarisiz oldu" gibi sabit
        // bir cumle gosteriyordu. Sunucu sebebi biliyorken kullanicinin
        // bilmemesi icin bir neden yok — ornegin "Invalid column name 'X'"
        // hatasini goren kullanici sorusunu duzeltebilir.
        string? failureReason = null;
        if (query.Status == "failed" && !string.IsNullOrWhiteSpace(query.ResultJson))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<JsonElement>(query.ResultJson);
                if (stored.TryGetProperty("error", out var errorProp))
                    failureReason = errorProp.GetString();
            }
            catch (JsonException)
            {
                // Bozuk kayit durumu bildirmeyi engellemesin.
            }
        }

        return Results.Ok(new
        {
            queryId = query.Id,
            status = query.Status,
            question = query.Question,
            createdAt = query.CreatedAt,
            completedAt = query.CompletedAt,
            error = failureReason
        });
    }

    private static async Task<IResult> GetQueryResult(
        Guid queryId,
        [FromServices] DataAnalysisDbContext db)
    {
        var query = await db.QueryHistories.FindAsync(queryId);
        if (query is null)
            return Results.NotFound(new { error = "Sorgu bulunamadı" });

        if (query.Status != "completed")
        {
            return Results.Ok(new
            {
                queryId = query.Id,
                status = query.Status,
                message = query.Status == "processing"
                    ? "Analiz devam ediyor..."
                    : "Analiz henüz tamamlanmadı"
            });
        }

        return Results.Ok(new
        {
            queryId = query.Id,
            status = query.Status,
            question = query.Question,
            result = JsonSerializer.Deserialize<JsonElement>(query.ResultJson),
            createdAt = query.CreatedAt,
            completedAt = query.CompletedAt
        });
    }

    private static async Task<IResult> GetQueryHistory(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db)
    {
        var config = await db.AnalysisConfigs
            .FirstOrDefaultAsync(c => c.ConnectionId == connectionId && c.IsActive);

        if (config is null)
            return Results.Ok(new { queries = Array.Empty<object>() });

        var queries = await db.QueryHistories
            .Where(q => q.ConfigId == config.Id)
            .OrderByDescending(q => q.CreatedAt)
            .Take(50)
            .Select(q => new
            {
                queryId = q.Id,
                question = q.Question,
                status = q.Status,
                createdAt = q.CreatedAt,
                completedAt = q.CompletedAt
            })
            .ToListAsync();

        return Results.Ok(new { queries });
    }
}

using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
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
        [FromServices] LlmAnalysisService llm,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IConfiguration configuration,
        [FromServices] IIdentityService identity,
        [FromServices] ILogger<LlmAnalysisService> logger)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        // Bekletici kapi. Onceden iki kapi vardi — Mongo'daki on analiz profili
        // ve Postgres'teki config — ve ikisi ayri ayri kontrol ediliyordu.
        // Artik tek adim, tek durum.
        var config = await db.AnalysisConfigs
            .Where(c => c.ConnectionId == request.ConnectionId
                     && c.CompanyId == scopedCompanyId
                     && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (config is null || config.Status != AgentAnalyzeEndpoints.AnalysisStatus.Ready)
        {
            return Results.BadRequest(new
            {
                error = config?.Status switch
                {
                    AgentAnalyzeEndpoints.AnalysisStatus.AwaitingAnswers =>
                        "Analiz soruları yanıtlanmayı bekliyor. SQL Bağlantı Ayarları'ndan tamamlayın.",
                    AgentAnalyzeEndpoints.AnalysisStatus.Analyzing =>
                        "Analiz sürüyor. Tamamlanınca sorgu gönderebilirsiniz.",
                    AgentAnalyzeEndpoints.AnalysisStatus.Failed =>
                        "Analiz başarısız oldu. SQL Bağlantı Ayarları'ndan tekrar çalıştırın.",
                    _ =>
                        "Bu bağlantı için analiz yapılmamış. Önce tabloları seçip 'Analiz Et' çalıştırın."
                },
                status = config?.Status ?? "none"
            });
        }

        // 2. Bağlantı bilgilerini al — sirket suzgeciyle: baska bir sirketin
        // baglanti kimligini bilen biri onun veritabanini sorgulayamasin.
        var savedConn = await db.SavedConnections
            .FirstOrDefaultAsync(c => c.Id == request.ConnectionId
                                   && c.CompanyId == scopedCompanyId
                                   && c.IsActive);

        if (savedConn is null)
            return Results.NotFound(new { error = "Bağlantı bulunamadı" });

        // 3. Soruyu semantik sozlukle birlikte LLM'e gonder → analiz parametreleri.
        // Sozluk, "Analiz Et" adiminin ciktisi: kolon adlari, ne anlama
        // geldikleri ve kullanicinin onlara ne diyebilecegi burada yaziyor.
        var translation = await llm.TranslateQuestionAsync(
            request.Question, config.ConfigJson, config.SchemaSummary);

        // Eksik yapilandirma bir cokme degil; 500 yerine 503 donuluyor ki
        // arayuz "sunucu hatasi" yerine sebebi gosterebilsin.
        if (!translation.Success)
        {
            return translation.IsConfigurationError
                ? Results.Problem(
                    detail: translation.Error,
                    title: "Yapay zekâ servisi yapılandırılmamış",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Problem(
                    detail: translation.Error,
                    title: "Soru analizi başarısız",
                    statusCode: StatusCodes.Status502BadGateway);
        }

        // Model soruyu cozemediginde `target_table`'i bilerek bos birakiyor —
        // ceviri prompt'unun 7. kurali bu. Bu sinyal okunmuyordu: istek yine de
        // PyCaret'e gidiyor, orada config'in ILK tablosu secilip analiz
        // ediliyordu. Yani sistem "anlamadim" dedigi anda kullaniciya rastgele
        // bir tablodan cikmis, dogru gorunen bir grafik gosteriyordu. Sorunun
        // neresinin anlasilmadigini sormak, uydurma cevaptan iyidir.
        if (!HasTargetTable(translation.Json, out var clarification))
        {
            logger.LogInformation(
                "Soru çözümlenemedi, kullanıcıya soruluyor: {Question}", request.Question);

            return Results.BadRequest(new
            {
                error = string.IsNullOrWhiteSpace(clarification)
                    ? "Sorunuzun hangi alanla ilgili olduğunu çözemedim. Hangi tabloyu "
                      + "ya da alanı kastettiğinizi yazar mısınız?"
                    : clarification,
                needsClarification = true
            });
        }

        // 4. QueryHistory kaydet
        var queryHistory = new QueryHistory
        {
            Id = Guid.NewGuid(),
            ConfigId = config.Id,
            UserId = config.UserId,
            Question = request.Question,
            PyCaretParamsJson = translation.Json,
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
                analysis_params_json = translation.Json,
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
            explanation = translation.Explanation,
            message = "Sorgunuz analiz edilmeye başlandı"
        });
    }

    /// <summary>
    /// Ceviri sonucunda hedef tablo secilmis mi. Secilmemisse modelin
    /// <c>description</c> alanina yazdigi eksik bilgi <paramref name="clarification"/>
    /// ile disari verilir — kullaniciya sorulacak sey odur.
    /// </summary>
    private static bool HasTargetTable(string translationJson, out string? clarification)
    {
        clarification = null;

        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(translationJson);
            if (doc.ValueKind != JsonValueKind.Object) return false;

            if (doc.TryGetProperty("target_table", out var table)
                && table.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(table.GetString()))
            {
                return true;
            }

            if (doc.TryGetProperty("description", out var description)
                && description.ValueKind == JsonValueKind.String)
            {
                clarification = description.GetString();
            }

            return false;
        }
        catch (JsonException)
        {
            clarification = "Soru analiz edilemedi — model geçerli bir yanıt üretmedi. "
                          + "Sorunuzu biraz daha açık yazıp tekrar dener misiniz?";
            return false;
        }
    }

    /// <summary>
    /// Sorgunun cagiran kullanicinin sirketine ait olup olmadigi.
    ///
    /// <see cref="QueryHistory"/> sirket bilgisini kendisi tasimiyor; sahiplik
    /// bagli oldugu <see cref="AnalysisConfig"/> uzerinden dogrulaniyor.
    ///
    /// Bu kontrol yoktu: kimligi dogrulanmis herhangi bir kullanici, bir
    /// sorgu kimligini bilmesi halinde baska bir sirketin analiz sonucunu —
    /// musteri verisinden uretilmis grafikleri ve calistirilan SQL'i —
    /// okuyabiliyordu. <c>SubmitQuery</c> suzgeci uyguluyordu, okuma uclari
    /// atlamisti.
    /// </summary>
    private static Task<bool> IsOwnedByCompanyAsync(
        DataAnalysisDbContext db, Guid configId, string companyId) =>
        db.AnalysisConfigs.AnyAsync(c => c.Id == configId && c.CompanyId == companyId);

    private static async Task<IResult> GetQueryStatus(
        Guid queryId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IConfiguration configuration,
        [FromServices] IIdentityService identity,
        [FromServices] ILogger<LlmAnalysisService> logger)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var query = await db.QueryHistories.FindAsync(queryId);

        // Baska sirkete ait kayitta da "bulunamadi" donuyor: 403 donmek
        // kaydin var oldugunu dogrulamak olurdu.
        if (query is null || !await IsOwnedByCompanyAsync(db, query.ConfigId, companyId.Value.ToString()))
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
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var query = await db.QueryHistories.FindAsync(queryId);
        if (query is null || !await IsOwnedByCompanyAsync(db, query.ConfigId, companyId.Value.ToString()))
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

        // Denetim izi: sonucun dogrulugunu degerlendirebilmek icin LLM'in
        // verdigi kararlar da doner. Grafigin dogru gorunmesi yeterli degil —
        // "hangi tabloya, hangi kolona gitti, nasil grupladi" gorulmeden
        // kalite olculemez.
        JsonElement? llmParameters = null;
        if (!string.IsNullOrWhiteSpace(query.PyCaretParamsJson))
        {
            try { llmParameters = JsonSerializer.Deserialize<JsonElement>(query.PyCaretParamsJson); }
            catch (JsonException) { /* bozuk kayit denetimi engellemesin */ }
        }

        return Results.Ok(new
        {
            queryId = query.Id,
            status = query.Status,
            question = query.Question,
            result = JsonSerializer.Deserialize<JsonElement>(query.ResultJson),
            llmParameters,
            createdAt = query.CreatedAt,
            completedAt = query.CompletedAt,
            durationMs = query.CompletedAt.HasValue
                ? (int)(query.CompletedAt.Value - query.CreatedAt).TotalMilliseconds
                : (int?)null
        });
    }

    /// <summary>
    /// Baglantinin sorgu gecmisi — kanvasin yeniden kurulabilmesi icin
    /// sonuclariyla birlikte.
    ///
    /// Iki sey degisti:
    ///
    /// 1. Gecmis artik AKTIF config'e degil BAGLANTIYA bagli. "Analiz Et" her
    ///    calistiginda yeni bir config uretilip eskisi pasiflesiyor; yalnizca
    ///    aktif config'e bakmak, yeniden analiz sonrasi butun gecmisi yok
    ///    gostermek demekti.
    ///
    /// 2. Sonuc govdesi de donuyor. Onceden yalnizca soru metni ve durum
    ///    donuyordu; kanvas grafikleri geri kuramadigi icin cikip giren
    ///    kullanici bos ekranla karsilasiyordu. Sonuclari ayri ayri cekmek
    ///    50 soru icin 51 istek demek olurdu.
    /// </summary>
    private static async Task<IResult> GetQueryHistory(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        var configIds = await db.AnalysisConfigs
            .Where(c => c.ConnectionId == connectionId && c.CompanyId == scopedCompanyId)
            .Select(c => c.Id)
            .ToListAsync();

        if (configIds.Count == 0)
            return Results.Ok(new { queries = Array.Empty<object>() });

        var rows = await db.QueryHistories
            .Where(q => configIds.Contains(q.ConfigId))
            .OrderByDescending(q => q.CreatedAt)
            .Take(HistoryLimit)
            .ToListAsync();

        // En yeni 50 kayit alinip tuval icin eskiden yeniye siralaniyor:
        // sorular sohbet sirasiyla okunmali.
        var queries = rows
            .OrderBy(q => q.CreatedAt)
            .Select(q => new
            {
                queryId = q.Id,
                question = q.Question,
                status = q.Status,
                createdAt = q.CreatedAt,
                completedAt = q.CompletedAt,
                result = TryParse(q.ResultJson),
                llmParameters = TryParse(q.PyCaretParamsJson),
            })
            .ToList();

        return Results.Ok(new { queries });
    }

    /// <summary>Kanvasa geri yuklenecek en fazla soru sayisi.</summary>
    private const int HistoryLimit = 50;

    private static JsonElement? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return null; }
    }
}

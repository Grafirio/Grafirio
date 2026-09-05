using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Application.Interfaces;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.QueryPolicy;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
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

        // Soru sormak analiz kosturmak demek: ANALYSIS.CREATE. Sonucu ve
        // gecmisi okumak READ ile yetiniyor — bir kullaniciya "analizleri
        // gorsun ama yenisini kosturmasin" denebilmeli.
        group.MapPost("/query", SubmitQuery)
            .RequirePermission(AppPermissions.AnalysisCreate)
            .WithName("SubmitAgentQuery")
            .WithDescription("Kullanıcı sorusunu LLM ile analiz edip PyCaret'e gönderir");

        group.MapGet("/query/{queryId:guid}/status", GetQueryStatus)
            .RequirePermission(AppPermissions.AnalysisRead)
            .WithName("GetQueryStatus")
            .WithDescription("Sorgu işlem durumunu kontrol eder");

        group.MapGet("/query/{queryId:guid}/result", GetQueryResult)
            .RequirePermission(AppPermissions.AnalysisRead)
            .WithName("GetQueryResult")
            .WithDescription("Sorgu sonucunu getirir");

        group.MapPost("/query/{queryId:guid}/cancel", CancelQuery)
            .RequirePermission(AppPermissions.AnalysisCreate)
            .WithName("CancelAgentQuery")
            .WithDescription("Cancels an owned analysis and blocks further SQL callbacks.");

        group.MapGet("/queries/{connectionId:guid}", GetQueryHistory)
            .RequirePermission(AppPermissions.AnalysisRead)
            .WithName("GetQueryHistory")
            .WithDescription("Bağlantıya ait sorgu geçmişini getirir");
    }

    /// <param name="ParentQueryId">
    /// Kullanicinin altina yazdigi onceki sorunun kimligi. Tuvalde bir soru
    /// dugumunun uzerinden devam edildiginde dolu gelir; sol panelden sorulan
    /// soru yeni konusma baslattigi icin bos gecer.
    ///
    /// Bu alan yoktu: tuvalde dugumler gorsel olarak birbirine bagliydi ama
    /// sunucuya giden istek { connectionId, question }'dan ibaretti — hangi
    /// dugumun altina yazildigi yola bile cikmiyordu.
    /// </param>
    public record QueryRequest(Guid ConnectionId, string Question, Guid? ParentQueryId = null);

    private static async Task<IResult> SubmitQuery(
        [FromBody] QueryRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] LlmAnalysisService llm,
        [FromServices] IPyCaretQueryService pycaret,
        [FromServices] ConnectionProfileStore profiles,
        [FromServices] IIdentityService identity,
        [FromServices] ILogger<LlmAnalysisService> logger,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var scopedCompanyId = companyId.Value.ToString();

        if (!pycaret.IsConfigured)
            return Results.Problem(detail: "Internal PyCaret authentication is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > MaxQuestionLength)
            return Results.BadRequest(new { error = "Question must contain between 1 and 2000 characters." });

        // Bekletici kapi. Onceden iki kapi vardi — Mongo'daki on analiz profili
        // ve Postgres'teki config — ve ikisi ayri ayri kontrol ediliyordu.
        // Artik tek adim, tek durum.
        var config = await db.AnalysisConfigs
            .Where(c => c.ConnectionId == request.ConnectionId
                     && c.CompanyId == scopedCompanyId
                     && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

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
                                   && c.IsActive, ct);

        if (savedConn is null)
            return Results.NotFound(new { error = "Bağlantı bulunamadı" });

        IReadOnlyList<string> selectedTables;
        try
        {
            selectedTables = QueryTableScope.RequireCurrentConfig(config.TablesJson,
                await profiles.GetSelectedTablesAsync(request.ConnectionId, scopedCompanyId, ct));
        }
        catch (QueryPolicyException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        try
        {
            if (AnalysisAnswers.CountPending(config.ConfigJson) != 0)
                return Results.Conflict(new { error = "Analiz soruları tamamlanmadan sorgu çalıştırılamaz." });
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Results.Conflict(new { error = "Analiz sözlüğü eski veya eksik. Seçili tablolar için 'Analiz Et'i yeniden çalıştırın." });
        }

        var originalConfigJson = config.ConfigJson;

        // 3. Konusmanin gecmisi. Zincirin kapsami BAGLANTI: "Analiz Et" her
        // calistiginda yeni bir config uretiliyor, dolayisiyla bir onceki tur
        // baska (eski) bir config'e bagli olabilir. Yalnizca aktif config'e
        // bakmak, yeniden analizden sonra konusmayi sifirlamak olurdu.
        //
        // Sirket suzgeci configIds'in kendisinde: baska bir sirketin sorgu
        // kimligini bilen biri o konusmayi kendi prompt'una tasiyamaz.
        var connectionConfigIds = await db.AnalysisConfigs
            .Where(c => c.ConnectionId == request.ConnectionId && c.CompanyId == scopedCompanyId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var conversation = await ConversationContext.LoadAsync(
            db, request.ParentQueryId, connectionConfigIds, ct);

        // Zincir kurulamadiysa bag da yazilmiyor. Istemci herhangi bir kimlik
        // gonderebilir; kapsam disi bir kimligi kayda gecirmek, hicbir zaman
        // cozulmeyecek bir isaretciyi kalici hale getirmek olurdu.
        var parentQueryId = conversation.Count > 0 ? request.ParentQueryId : null;

        // 4. Soruyu semantik sozlukle birlikte LLM'e gonder → analiz parametreleri.
        // Sozluk, "Analiz Et" adiminin ciktisi: kolon adlari, ne anlama
        // geldikleri ve kullanicinin onlara ne diyebilecegi burada yaziyor.
        var translation = await llm.TranslateQuestionAsync(
            request.Question, config.ConfigJson, config.SchemaSummary,
            ConversationContext.Render(conversation), ct);

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
        List<RelationshipProposal> proposals;
        try
        {
            AgentQueryTableScope.Validate(translation.Json, config.ConfigJson, selectedTables);
            proposals = RelationshipDictionary.ReadProposals(translation.Json, config.ConfigJson);
        }
        catch (QueryPolicyException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        // Translation can outlive a selection or dictionary edit; reject stale authority before persisting any turn.
        await db.Entry(config).ReloadAsync(ct);
        if (!config.IsActive || config.Status != AgentAnalyzeEndpoints.AnalysisStatus.Ready || config.ConfigJson != originalConfigJson)
            return Results.Conflict(new { error = "Analysis dictionary changed during translation; retry the question." });
        try
        {
            var currentTables = await profiles.GetSelectedTablesAsync(request.ConnectionId, scopedCompanyId, ct);
            QueryTableScope.RequireCurrentConfig(JsonSerializer.Serialize(selectedTables), currentTables);
            QueryTableScope.RequireCurrentConfig(config.TablesJson, currentTables);
        }
        catch (QueryPolicyException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }

        var hasTargetTable = HasTargetTable(translation.Json, out var clarification);
        if (proposals.Count > 0)
        {
            // A model proposal is never authority to execute a join; the permission-protected learn endpoint applies it.
            clarification = "Şu eşleşmeleri öneriyorum: "
                + string.Join("; ", proposals.Select(p => $"{p.FromTable}.{p.FromColumn} → {p.ToTable}.{p.ToColumn}"))
                + ". Onayladığınız eşleşmeler doğrulanıp mevcut analize uygulanacak. "
                + (proposals.Count == 1 ? "Onaylıyor musunuz?" : "Her birini ayrı seçebilir veya ‘hepsini onaylıyorum’ yazabilirsiniz.");
        }

        if (!hasTargetTable || proposals.Count > 0)
        {
            logger.LogInformation(
                "Query translation requires clarification for connection {ConnectionId}", request.ConnectionId);

            var asked = string.IsNullOrWhiteSpace(clarification)
                ? "Sorunuzun hangi alanla ilgili olduğunu çözemedim. Hangi tabloyu "
                  + "ya da alanı kastettiğinizi yazar mısınız?"
                : clarification;
            asked = asked[..Math.Min(asked.Length, MaxQuestionLength)];

            // Netlestirme turu da yaziliyor. Onceden bu dönüş, QueryHistory
            // kaydinin olusturuldugu satirdan ONCE geliyordu: sistem soruyu
            // sorup sordugunu unutuyordu. Kullanici cevap verdiginde ortada
            // cevaplanacak bir soru olduguna dair kayit yoktu — baglami
            // tasisak bile tasinacak bir sey olmayacakti.
            var clarificationRow = new QueryHistory
            {
                Id = Guid.NewGuid(),
                ConfigId = config.Id,
                UserId = config.UserId,
                Question = request.Question,
                ParentQueryId = parentQueryId,
                PyCaretParamsJson = translation.Json,
                ClarificationQuestion = asked,
                Status = ConversationContext.ClarificationStatus,
                CreatedAt = DateTime.UtcNow,
                // Tur burada bitiyor: bekleyen bir is yok, bekleyen bir cevap var.
                CompletedAt = DateTime.UtcNow
            };

            clarificationRow.ResultJson = new JsonObject
            {
                [PyCaretExecutionContext.PropertyName] = PyCaretExecutionContext.Create(clarificationRow, config).ToJson(),
                ["needsClarification"] = true,
                ["clarificationQuestion"] = asked,
                ["pendingConfirmations"] = JsonSerializer.SerializeToNode(proposals, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            }.ToJsonString();

            db.QueryHistories.Add(clarificationRow);
            await db.SaveChangesAsync(ct);

            return Results.BadRequest(new
            {
                error = asked,
                needsClarification = true,
                pendingConfirmations = proposals,
                // Kimlik disari veriliyor ki kullanicinin cevabi bu tura
                // baglanabilsin; zincirin halkasi bu.
                queryId = clarificationRow.Id
            });
        }

        // Persist authority before the worker can make its first callback.
        var queryHistory = new QueryHistory
        {
            Id = Guid.NewGuid(),
            ConfigId = config.Id,
            UserId = config.UserId,
            Question = request.Question,
            ParentQueryId = parentQueryId,
            PyCaretParamsJson = translation.Json,
            Status = "processing",
            CreatedAt = DateTime.UtcNow
        };

        queryHistory.ResultJson = new JsonObject
        {
            [PyCaretExecutionContext.PropertyName] = PyCaretExecutionContext.Create(queryHistory, config).ToJson()
        }.ToJsonString();

        db.QueryHistories.Add(queryHistory);
        await db.SaveChangesAsync(ct);
        await pycaret.SubmitAsync(queryHistory, config, ct);

        return Results.Ok(new
        {
            success = queryHistory.Status != "failed",
            queryId = queryHistory.Id,
            status = queryHistory.Status,
            error = StoredField(queryHistory, "error"),
            clarificationQuestion = queryHistory.ClarificationQuestion,
            pendingConfirmations = StoredField(queryHistory, "pendingConfirmations"),
            explanation = translation.Explanation,
            message = StoredField(queryHistory, "message")
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
            // Emniyet agi: bozuk yanit artik cevirinin kendisinde yakalaniyor
            // ve buraya gelmiyor. Geldigi gun de kullaniciya DOGRU seyi
            // soylemeli — "sorunuzu daha acik yazin" yanlis ogutti, cunku
            // sorunun okunakligiyla ilgisi yok.
            clarification = "Yapay zekânın yanıtı okunamadı. Aynı soruyu tekrar "
                          + "sormayı deneyin; sorunuzda bir sorun yok.";
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
        [FromServices] IPyCaretQueryService pycaret,
        [FromServices] IIdentityService identity,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var query = await db.QueryHistories.FindAsync(queryId);

        // Baska sirkete ait kayitta da "bulunamadi" donuyor: 403 donmek
        // kaydin var oldugunu dogrulamak olurdu.
        if (query is null || !await IsOwnedByCompanyAsync(db, query.ConfigId, companyId.Value.ToString()))
            return Results.NotFound(new { error = "Sorgu bulunamadı" });

        await pycaret.RefreshAsync(query, ct);
        return QueryState(query);
    }

    private static async Task<IResult> CancelQuery(
        Guid queryId, DataAnalysisDbContext db, IPyCaretQueryService pycaret,
        IIdentityService identity, CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();
        var query = await db.QueryHistories.FindAsync([queryId], ct);
        if (query is null || !await IsOwnedByCompanyAsync(db, query.ConfigId, companyId.Value.ToString()))
            return Results.NotFound(new { error = "Sorgu bulunamadı" });
        await pycaret.CancelAsync(query, ct);
        return QueryState(query);
    }

    private static IResult QueryState(QueryHistory query) => Results.Ok(new
    {
        queryId = query.Id,
        status = query.Status,
        question = query.Question,
        createdAt = query.CreatedAt,
        completedAt = query.CompletedAt,
        error = StoredField(query, "error"),
        message = StoredField(query, "message"),
        clarificationQuestion = query.ClarificationQuestion,
        needsClarification = query.Status == ConversationContext.ClarificationStatus,
        pendingConfirmations = StoredField(query, "pendingConfirmations")
    });

    private static async Task<IResult> GetQueryResult(
        Guid queryId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        [FromServices] IPyCaretQueryService pycaret,
        CancellationToken ct)
    {
        var companyId = identity.CurrentCompanyId;
        if (companyId is null) return Results.Forbid();

        var query = await db.QueryHistories.FindAsync(queryId);
        if (query is null || !await IsOwnedByCompanyAsync(db, query.ConfigId, companyId.Value.ToString()))
            return Results.NotFound(new { error = "Sorgu bulunamadı" });

        await pycaret.RefreshAsync(query, ct);
        if (query.Status != "completed") return QueryState(query);

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
                // Zincir de geri donuyor: tuval yeniden yuklendiginde takip
                // sorusu hangi turun altinda soruldusa oraya baglanmali,
                // yoksa konusma bagimsiz sorular yiginina donusuyor.
                parentQueryId = q.ParentQueryId,
                clarificationQuestion = q.ClarificationQuestion,
                error = StoredField(q, "error"),
                message = StoredField(q, "message"),
                pendingConfirmations = StoredField(q, "pendingConfirmations"),
            })
            .ToList();

        var activeDictionary = await db.AnalysisConfigs.AsNoTracking()
            .Where(c => c.ConnectionId == connectionId && c.CompanyId == scopedCompanyId && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => c.ConfigJson).FirstOrDefaultAsync();
        var relationshipDecisions = RelationshipDictionary.AppliedDecisions(activeDictionary ?? "{}");
        return Results.Ok(new { queries, relationshipDecisions });
    }

    /// <summary>Kanvasa geri yuklenecek en fazla soru sayisi.</summary>
    private const int HistoryLimit = 50;
    private const int MaxQuestionLength = 2000;

    private static JsonElement? StoredField(QueryHistory query, string name)
    {
        var result = TryParse(query.ResultJson);
        return result is { ValueKind: JsonValueKind.Object } && result.Value.TryGetProperty(name, out var value)
            ? value : null;
    }

    private static JsonElement? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return null; }
    }
}

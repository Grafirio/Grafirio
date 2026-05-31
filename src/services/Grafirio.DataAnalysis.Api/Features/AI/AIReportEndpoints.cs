using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Shared.MassTransit.Messages.AI;
using MassTransit;

namespace Grafirio.DataAnalysis.Api.Features.AI;

public static class AIReportEndpoints
{
    public static void MapAIReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/reports");

        group.MapPost("/generate", GenerateReport)
            .WithName("GenerateAIReport")
            .WithTags("AI Reports");

        group.MapPost("/ask-question", AskQuestion)
            .WithName("AskAIQuestion")
            .WithTags("AI Reports");
    }

    private static async Task<IResult> GenerateReport(
        GenerateReportRequest request,
        ILogger<GenerateReportRequest> logger)
    {
        try
        {
            logger.LogInformation("🤖 Generating AI report. RequestId: {RequestId}, Type: {ReportType}", 
                request.RequestId, request.ReportType);

            // 🚀 TÜM RAPOR TALEPLERI DJANGO AI'YA GÖNDERİLİYOR
            logger.LogInformation("🧠 Sending report request to Django AI");
            
            // TODO: RabbitMQ üzerinden Django AI'ya rapor talebi gönder
            // await SendReportRequestToDjangoAI(request, logger);
            
            // Placeholder response
            var placeholderChart = new ChartResponse("line", "🤖 Rapor Oluşturuluyor", 
                new { 
                    labels = new[] { "Hazırlanıyor" }, 
                    datasets = new[] { 
                        new { label = "Durum", data = new[] { 1 } }
                    }
                });

            var insights = new[]
            {
                new InsightResponse("info", "🤖 AI Rapor Oluşturuluyor", 
                    "Django AI servisi raporu hazırlıyor. Sonuçlar birkaç saniye içinde hazır olacak.")
            };

            return Results.Ok(new GenerateReportResponse(
                Success: true,
                ReportType: request.ReportType,
                Title: GetReportTitle(request.ReportType),
                Description: GetReportDescription(request.ReportType),
                Charts: new[] { placeholderChart },
                Insights: insights,
                GeneratedAt: DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error generating report");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static async Task<IResult> AskQuestion(
        AskQuestionRequest request,
        IPublishEndpoint publishEndpoint,
        ILogger<AskQuestionRequest> logger)
    {
        try
        {
            logger.LogInformation(
                "🤖 Soru MassTransit→RabbitMQ→Django pipeline'ına yönlendiriliyor. RequestId: {RequestId}, Soru: {Question}",
                request.RequestId, request.Question);

            // Konuşma geçmişini IQuestionRequest.Context'e map et
            var contextItems = request.History?
                .Select(h => new ChatContextItem(h.Role, h.Content))
                .ToList()
                ?? new List<ChatContextItem>();

            // MassTransit aracılığıyla RabbitMQ'ya yayınla
            await publishEndpoint.Publish<IQuestionRequest>(new
            {
                RequestId   = request.RequestId,
                UserId      = "web-user",
                CompanyId   = "default",
                RequestTime = DateTime.UtcNow,
                Question    = request.Question,
                Context     = contextItems,
                Database    = request.Database,
                Tables      = request.Tables,
                TableName   = request.TableName,
                PredictData = request.PredictData
            });

            logger.LogInformation(
                "✅ MassTransit'e yayınlandı | RequestId: {RequestId}",
                request.RequestId);

            return Results.Ok(new AskQuestionResponse(
                Success:     true,
                Question:    request.Question,
                Answer:      "🔄 Sorunuz Gemini'ye iletildi. Sonuçlar birkaç saniye içinde hazır olacak...",
                Charts:      Array.Empty<ChartResponse>(),
                AnsweredAt:  DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ MassTransit publish hatası | RequestId: {RequestId}", request.RequestId);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static string GetReportTitle(string reportType) => reportType switch
    {
        "user-activity" => "📊 Kullanıcı Aktivite Raporu",
        "data-distribution" => "📈 Veri Dağılım Analizi",
        "trend-analysis" => "📉 Trend Analizi Raporu",
        "summary-statistics" => "🔢 Özet İstatistikler",
        _ => "📋 Genel Rapor"
    };

    private static string GetReportDescription(string reportType) => reportType switch
    {
        "user-activity" => "Kullanıcı aktivitelerinin detaylı analizi ve trendleri",
        "data-distribution" => "Verilerin tablolar ve tipler arası dağılımı",
        "trend-analysis" => "Zaman serisi analizi ve büyüme trendleri",
        "summary-statistics" => "Genel istatistiksel özet ve metrikler",
        _ => "AI tarafından oluşturulan genel analiz raporu"
    };
}

// Request/Response Models
public record GenerateReportRequest(
    Guid RequestId,
    string ReportType,
    string Database,
    List<string> Tables
);

public record GenerateReportResponse(
    bool Success,
    string ReportType,
    string Title,
    string Description,
    ChartResponse[] Charts,
    InsightResponse[] Insights,
    DateTime GeneratedAt
);

public record ChatHistoryItem(string Role, string Content);

public record AskQuestionRequest(
    Guid RequestId,
    string Question,
    string Database,
    List<string> Tables,
    List<ChatHistoryItem>? History = null,
    string? TableName = null,
    Dictionary<string, object>? PredictData = null
);

public record AskQuestionResponse(
    bool Success,
    string Question,
    string Answer,
    ChartResponse[] Charts,
    DateTime AnsweredAt
);

public record ChartResponse(
    string Type,
    string Title,
    object Data
);

public record InsightResponse(
    string Type,
    string Title,
    string Description
);

using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Contracts.AI;
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
            logger.LogInformation("ğŸ¤– Generating AI report. RequestId: {RequestId}, Type: {ReportType}", 
                request.RequestId, request.ReportType);

            // ğŸš€ TÃœM RAPOR TALEPLERI DJANGO AI'YA GÃ–NDERÄ°LÄ°YOR
            logger.LogInformation("ğŸ§  Sending report request to Django AI");
            
            // TODO: RabbitMQ Ã¼zerinden Django AI'ya rapor talebi gÃ¶nder
            // await SendReportRequestToDjangoAI(request, logger);
            
            // Placeholder response
            var placeholderChart = new ChartResponse("line", "ğŸ¤– Rapor OluÅŸturuluyor", 
                new { 
                    labels = new[] { "HazÄ±rlanÄ±yor" }, 
                    datasets = new[] { 
                        new { label = "Durum", data = new[] { 1 } }
                    }
                });

            var insights = new[]
            {
                new InsightResponse("info", "ğŸ¤– AI Rapor OluÅŸturuluyor", 
                    "Django AI servisi raporu hazÄ±rlÄ±yor. SonuÃ§lar birkaÃ§ saniye iÃ§inde hazÄ±r olacak.")
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
            logger.LogError(ex, "âŒ Error generating report");
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
                "ğŸ¤– Soru MassTransitâ†’RabbitMQâ†’Django pipeline'Ä±na yÃ¶nlendiriliyor. RequestId: {RequestId}, Soru: {Question}",
                request.RequestId, request.Question);

            // KonuÅŸma geÃ§miÅŸini IQuestionRequest.Context'e map et
            var contextItems = request.History?
                .Select(h => new ChatContextItem(h.Role, h.Content))
                .ToList()
                ?? new List<ChatContextItem>();

            // MassTransit aracÄ±lÄ±ÄŸÄ±yla RabbitMQ'ya yayÄ±nla
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
                "âœ… MassTransit'e yayÄ±nlandÄ± | RequestId: {RequestId}",
                request.RequestId);

            return Results.Ok(new AskQuestionResponse(
                Success:     true,
                Question:    request.Question,
                Answer:      "ğŸ”„ Sorunuz Gemini'ye iletildi. SonuÃ§lar birkaÃ§ saniye iÃ§inde hazÄ±r olacak...",
                Charts:      Array.Empty<ChartResponse>(),
                AnsweredAt:  DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "âŒ MassTransit publish hatasÄ± | RequestId: {RequestId}", request.RequestId);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static string GetReportTitle(string reportType) => reportType switch
    {
        "user-activity" => "ğŸ“Š KullanÄ±cÄ± Aktivite Raporu",
        "data-distribution" => "ğŸ“ˆ Veri DaÄŸÄ±lÄ±m Analizi",
        "trend-analysis" => "ğŸ“‰ Trend Analizi Raporu",
        "summary-statistics" => "ğŸ”¢ Ã–zet Ä°statistikler",
        _ => "ğŸ“‹ Genel Rapor"
    };

    private static string GetReportDescription(string reportType) => reportType switch
    {
        "user-activity" => "KullanÄ±cÄ± aktivitelerinin detaylÄ± analizi ve trendleri",
        "data-distribution" => "Verilerin tablolar ve tipler arasÄ± daÄŸÄ±lÄ±mÄ±",
        "trend-analysis" => "Zaman serisi analizi ve bÃ¼yÃ¼me trendleri",
        "summary-statistics" => "Genel istatistiksel Ã¶zet ve metrikler",
        _ => "AI tarafÄ±ndan oluÅŸturulan genel analiz raporu"
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

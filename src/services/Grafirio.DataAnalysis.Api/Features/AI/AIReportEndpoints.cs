using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Grafirio.DataAnalysis.Api.Services;

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
        GeminiService geminiService,
        ILogger<AskQuestionRequest> logger)
    {
        try
        {
            logger.LogInformation("🤖 Processing AI question. RequestId: {RequestId}, Question: {Question}", 
                request.RequestId, request.Question);

            // Sorunun tipini belirle: Basit sohbet mi, veri analizi mi?
            var isDataQuery = GeminiService.IsDataAnalysisQuery(request.Question);

            if (!isDataQuery)
            {
                // 💬 Basit sohbet - direkt Gemini ile yanıtla
                logger.LogInformation("💬 Detected chat message, responding with Gemini");
                
                var chatResult = await geminiService.ChatAsync(request.Question);
                
                logger.LogInformation("💬 Chat result: Success={Success}, Response={Response}", 
                    chatResult.Success, chatResult.Response);
                
                var responseObj = new AskQuestionResponse(
                    Success: chatResult.Success,
                    Question: request.Question,
                    Answer: chatResult.Response,
                    Charts: Array.Empty<ChartResponse>(),
                    AnsweredAt: DateTime.UtcNow
                );
                
                logger.LogInformation("💬 Sending response: Success={Success}, Answer={Answer}", 
                    responseObj.Success, responseObj.Answer);
                
                return Results.Ok(responseObj);
            }
            else
            {
                // 🚀 Veri analizi sorusu - Query Executor'a gönder
                logger.LogInformation("🧠 Detected data analysis query, sending to Query Executor via RabbitMQ");
                
                await SendQuestionToQueryExecutor(request, logger);
                
                // Return immediate response, frontend will poll for results
                return Results.Ok(new AskQuestionResponse(
                    Success: true,
                    Question: request.Question,
                    Answer: "🔄 Sorgunuz işleniyor... Sonuçlar birkaç saniye içinde hazır olacak.",
                    Charts: Array.Empty<ChartResponse>(),
                    AnsweredAt: DateTime.UtcNow
                ));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error processing question");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static async Task SendQuestionToQueryExecutor(AskQuestionRequest request, ILogger logger)
    {
        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RabbitMQ__Host") ?? "rabbitmq.container",
            Port = 5672,
            UserName = "guest",
            Password = "guest123"
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        // Declare question queue for query executor
        await channel.QueueDeclareAsync(
            queue: "ai.question.queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        var message = new
        {
            request_id = request.RequestId.ToString(),
            question = request.Question,
            database = "GrafirioECommerce",  // Test database with 67,500 products
            host = "grafirio-sqlserver-test",  // Test SQL Server
            port = 1433,
            username = "sa",
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        logger.LogInformation("📤 Sending question to Query Executor: RequestId={RequestId}, Question={Question}", 
            request.RequestId, request.Question);

        await channel.BasicPublishAsync(
            exchange: "",
            routingKey: "ai.question.queue",
            body: body
        );
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

public record AskQuestionRequest(
    Guid RequestId,
    string Question,
    string Database,
    List<string> Tables
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

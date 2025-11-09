using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

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
        ILogger<AskQuestionRequest> logger)
    {
        try
        {
            logger.LogInformation("🤖 Processing AI question. RequestId: {RequestId}, Question: {Question}", 
                request.RequestId, request.Question);

            // 🚀 TÜM SORULARI DJANGO AI'YA GÖNDER
            // Manuel keyword matching yerine AI'nın doğal dil işleme gücünü kullan
            logger.LogInformation("🧠 Sending all questions to Django AI for intelligent processing");
            
            await SendQuestionToDjangoAI(request, logger);
            
            // WebSocket veya polling ile cevap gelecek
            // Şimdilik placeholder cevap
            var placeholderChart = new ChartResponse("line", "🤖 AI Analizi Devam Ediyor", 
                new { 
                    labels = new[] { "Analiz Ediliyor" }, 
                    datasets = new[] { 
                        new { label = "Durum", data = new[] { 1 } }
                    }
                });

            return Results.Ok(new AskQuestionResponse(
                Success: true,
                Question: request.Question,
                Answer: "🤖 Sorunuz Django AI tarafından analiz ediliyor. NLP modeli veritabanı şemasını ve sorunuzu anlayıp " +
                        "SQL sorgusu oluşturuyor. Cevap birkaç saniye içinde hazır olacak.",
                Charts: new[] { placeholderChart },
                AnsweredAt: DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error processing question");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static async Task SendQuestionToDjangoAI(AskQuestionRequest request, ILogger logger)
    {
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest123"
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            exchange: "ai.requests",
            type: ExchangeType.Topic,
            durable: true
        );

        var message = new
        {
            request_id = request.RequestId.ToString(),
            question = request.Question,
            database = request.Database,
            tables = request.Tables,
            request_type = "question",
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        logger.LogInformation("📤 Sending question to Django AI: {Size} bytes", body.Length);

        await channel.BasicPublishAsync(
            exchange: "ai.requests",
            routingKey: "ai.request.question",
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

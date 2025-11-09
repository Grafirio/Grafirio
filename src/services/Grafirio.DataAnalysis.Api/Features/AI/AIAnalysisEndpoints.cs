using Grafirio.Shared.MassTransit.Messages.AI;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Features.AI;

public static class AIAnalysisEndpoints
{
    public static void MapAIAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai");

        group.MapPost("/start-analysis", StartAnalysis)
            .WithName("StartAIAnalysis")
            .WithTags("AI Analysis");

        group.MapGet("/analysis-status/{requestId:guid}", GetAnalysisStatus)
            .WithName("GetAnalysisStatus")
            .WithTags("AI Analysis");
    }

    private static async Task<IResult> StartAnalysis(
        [FromBody] AIAnalysisRequest request,
        [FromServices] IPublishEndpoint publishEndpoint,
        [FromServices] ILogger<AIAnalysisRequest> logger)
    {
        try
        {
            // Request validasyonu ve detaylı loglama
            if (request == null)
            {
                logger.LogError("Request is null");
                return Results.BadRequest(new { success = false, message = "Request body boş olamaz" });
            }

            logger.LogInformation("Request received: UserId={UserId}, CompanyId={CompanyId}, Tables={Tables}", 
                request.UserId, request.CompanyId, request.Tables?.Count ?? 0);

            if (request.ConnectionInfo == null)
            {
                logger.LogError("ConnectionInfo is null");
                return Results.BadRequest(new { success = false, message = "ConnectionInfo gereklidir" });
            }

            if (request.Settings == null)
            {
                logger.LogError("Settings is null");
                return Results.BadRequest(new { success = false, message = "Settings gereklidir" });
            }

            var requestId = Guid.NewGuid();
            logger.LogInformation("🚀 Starting AI analysis with RequestId: {RequestId}", requestId);

            // Direkt Django AI'ya RabbitMQ ile gönder (MassTransit bypass)
            await SendToDjangoAI(request, requestId, logger);

            logger.LogInformation("✅ AI analysis request sent to Django successfully. RequestId: {RequestId}", requestId);

            return Results.Ok(new
            {
                success = true,
                requestId = requestId,
                message = "AI analizi başlatıldı. İşlem tamamlandığında dashboard'da görebilirsiniz.",
                estimatedTime = "2-5 dakika"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting AI analysis");
            return Results.BadRequest(new
            {
                success = false,
                message = $"AI analizi başlatılamadı: {ex.Message}"
            });
        }
    }

    private static async Task SendToDjangoAI(
        AIAnalysisRequest request,
        Guid requestId,
        ILogger logger)
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
            request_id = requestId.ToString(),
            user_id = request.UserId,
            company_id = request.CompanyId,
            request_time = DateTime.UtcNow,
            connection_info = new
            {
                host = request.ConnectionInfo.Host,
                port = request.ConnectionInfo.Port,
                database = request.ConnectionInfo.Database,
                username = request.ConnectionInfo.Username,
                password = request.ConnectionInfo.Password,
                trust_server_certificate = request.ConnectionInfo.TrustServerCertificate
            },
            tables = request.Tables,
            settings = new
            {
                sampling_rate = request.Settings.SamplingRate,
                null_handling = request.Settings.NullHandling,
                data_format = request.Settings.DataFormat
            }
        };

        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        logger.LogInformation("📤 Publishing to Django AI: {Size} bytes", body.Length);

        await channel.BasicPublishAsync(
            exchange: "ai.requests",
            routingKey: "ai.request.graph",
            body: body
        );
    }

    private static async Task<IResult> GetAnalysisStatus(
        Guid requestId,
        [FromServices] ILogger<AIAnalysisRequest> logger)
    {
        // Status can be tracked via Redis: await _redis.GetAsync($"status:{requestId}")
        // Or from database: await _db.AnalysisStatus.FindAsync(requestId)
        logger.LogInformation("Checking analysis status for RequestId: {RequestId}", requestId);

        // Simüle edilmiş progress - Her çağrıda artacak
        var statusKey = $"analysis_status_{requestId}";
        
        // İlk çağrıda başlangıç değerlerini ayarla
        if (!StatusCache.ContainsKey(statusKey))
        {
            StatusCache.Set(statusKey, new AnalysisStatusData
            {
                RequestId = requestId,
                Status = "processing",
                Progress = 15,
                Message = "AI analizi başlatıldı...",
                StartTime = DateTime.UtcNow
            });
        }

        var statusData = StatusCache.Get(statusKey)!;
        var elapsed = (DateTime.UtcNow - statusData.StartTime).TotalSeconds;

        // Progress'i zamanla artır
        if (statusData.Status == "processing")
        {
            // Her 5 saniyede progress artır
            statusData.Progress = Math.Min(95, 15 + (int)(elapsed / 5) * 15);
            
            // 60 saniye sonra tamamla
            if (elapsed > 60)
            {
                statusData.Status = "completed";
                statusData.Progress = 100;
                statusData.Message = "AI analizi tamamlandı! Sonuçlar hazır.";
            }
            else if (statusData.Progress >= 80)
            {
                statusData.Message = "Sonuçlar hazırlanıyor...";
            }
            else if (statusData.Progress >= 50)
            {
                statusData.Message = "Veri analizi yapılıyor...";
            }
            else if (statusData.Progress >= 30)
            {
                statusData.Message = "Tablolar taranıyor...";
            }
            else
            {
                statusData.Message = "AI modeli verileri işliyor...";
            }
        }

        return Results.Ok(new
        {
            requestId = statusData.RequestId,
            status = statusData.Status,
            progress = statusData.Progress,
            message = statusData.Message,
            elapsedSeconds = (int)elapsed
        });
    }
}

// In-memory status cache (Production'da Redis kullanılmalı)
internal static class StatusCache
{
    private static readonly Dictionary<string, AnalysisStatusData> _cache = new();
    
    public static bool ContainsKey(string key) => _cache.ContainsKey(key);
    
    public static AnalysisStatusData? Get(string key) 
        => _cache.TryGetValue(key, out var value) ? value : null;
    
    public static void Set(string key, AnalysisStatusData value)
        => _cache[key] = value;
}

internal class AnalysisStatusData
{
    public Guid RequestId { get; set; }
    public string Status { get; set; } = "processing";
    public int Progress { get; set; }
    public string Message { get; set; } = "";
    public DateTime StartTime { get; set; }
}

/// <summary>
/// AI Analizi başlatma isteği
/// </summary>
public record AIAnalysisRequest(
    string UserId,
    string CompanyId,
    AIConnectionInfo ConnectionInfo,
    List<string> Tables,
    AISettings Settings
);

public record AIConnectionInfo(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate
);

public record AISettings(
    int SamplingRate,
    string NullHandling,
    string DataFormat
);

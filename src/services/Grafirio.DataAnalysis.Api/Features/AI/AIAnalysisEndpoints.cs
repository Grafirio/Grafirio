using Grafirio.Shared.MassTransit.Messages.AI;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        
        group.MapPost("/update-progress", UpdateProgress)
            .WithName("UpdateProgress")
            .WithTags("AI Analysis");
        
        group.MapPost("/analysis-result", SaveAnalysisResult)
            .WithName("SaveAnalysisResult")
            .WithTags("AI Analysis");
    }

    private static async Task<IResult> StartAnalysis(
        [FromBody] AIAnalysisRequest request,
        [FromServices] IPublishEndpoint publishEndpoint,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<AIAnalysisRequest> logger)
    {
        try
        {
            // Request validasyonu
            if (request == null)
            {
                logger.LogError("Request is null");
                return Results.BadRequest(new { success = false, message = "Request body boş olamaz" });
            }

            logger.LogInformation("Request received: UserId={UserId}, ConnectionId={ConnectionId}, Tables={Tables}", 
                request.UserId, request.ConnectionId, request.Tables?.Count ?? 0);

            if (request.ConnectionId == Guid.Empty)
            {
                logger.LogError("ConnectionId is empty");
                return Results.BadRequest(new { success = false, message = "ConnectionId gereklidir" });
            }

            if (request.Settings == null)
            {
                logger.LogError("Settings is null");
                return Results.BadRequest(new { success = false, message = "Settings gereklidir" });
            }

            // Get connection from database
            var connection = await db.SavedConnections
                .FirstOrDefaultAsync(c => c.Id == request.ConnectionId && c.UserId == request.UserId && c.IsActive);

            if (connection == null)
            {
                logger.LogError("Connection not found: {ConnectionId}", request.ConnectionId);
                return Results.BadRequest(new { success = false, message = "Bağlantı bulunamadı" });
            }

            // Decrypt password
            logger.LogInformation("🔐 Decrypting password for connection: {ConnectionId}, Encrypted length: {Length}", 
                connection.Id, connection.EncryptedPassword?.Length ?? 0);
            
            var decryptedPassword = EncryptionHelper.Decrypt(connection.EncryptedPassword);
            
            if (string.IsNullOrEmpty(decryptedPassword))
            {
                logger.LogError("❌ Decryption failed or password is empty for connection: {ConnectionId}", connection.Id);
                return Results.BadRequest(new { success = false, message = "Şifre çözülemedi" });
            }
            
            logger.LogInformation("✅ Password decrypted successfully. Length: {Length}", decryptedPassword.Length);

            var requestId = Guid.NewGuid();
            logger.LogInformation("🚀 Starting AI analysis with RequestId: {RequestId}, Database: {Database}", 
                requestId, connection.Database);

            // Create connection info from saved connection
            var connectionInfo = new AIConnectionInfo(
                connection.Host,
                connection.Port,
                connection.Database,
                connection.Username,
                decryptedPassword,
                connection.TrustServerCertificate
            );

            // Send to Django AI via RabbitMQ
            await SendToDjangoAI(connectionInfo, request, requestId, logger);

            // Update last connected time
            connection.LastConnectedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

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
        AIConnectionInfo connectionInfo,
        AIAnalysisRequest request,
        Guid requestId,
        ILogger logger)
    {
        var factory = new ConnectionFactory
        {
            HostName = "rabbitmq.container",
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
                host = connectionInfo.Host,
                port = connectionInfo.Port,
                database = connectionInfo.Database,
                username = connectionInfo.Username,
                password = connectionInfo.Password,
                trust_server_certificate = connectionInfo.TrustServerCertificate
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
    
    private static Task<IResult> UpdateProgress(
        [FromBody] ProgressUpdate update,
        [FromServices] ILogger<AIAnalysisRequest> logger)
    {
        logger.LogInformation("Updating progress for RequestId: {RequestId} - {Progress}% - {Message}", 
            update.RequestId, update.Progress, update.Message);
        
        var statusKey = $"analysis_status_{update.RequestId}";
        
        if (StatusCache.ContainsKey(statusKey))
        {
            var statusData = StatusCache.Get(statusKey)!;
            statusData.Progress = update.Progress;
            statusData.Message = update.Message;
            
            logger.LogInformation("Progress updated: {RequestId} - {Progress}%", update.RequestId, update.Progress);
        }
        else
        {
            // Create new status entry
            StatusCache.Set(statusKey, new AnalysisStatusData
            {
                RequestId = update.RequestId,
                Status = "processing",
                Progress = update.Progress,
                Message = update.Message,
                StartTime = DateTime.UtcNow
            });
            
            logger.LogInformation("New status created: {RequestId}", update.RequestId);
        }
        
        return Task.FromResult(Results.Ok(new { success = true }));
    }
    
    private static Task<IResult> SaveAnalysisResult(
        [FromBody] AnalysisResult result,
        [FromServices] ILogger<AIAnalysisRequest> logger)
    {
        logger.LogInformation("Saving analysis result for RequestId: {RequestId} - Status: {Status}", 
            result.RequestId, result.Status);
        
        var statusKey = $"analysis_status_{result.RequestId}";
        
        if (StatusCache.ContainsKey(statusKey))
        {
            var statusData = StatusCache.Get(statusKey)!;
            statusData.Status = result.Status;
            statusData.Progress = result.Status == "completed" ? 100 : statusData.Progress;
            statusData.Message = result.Status == "completed" 
                ? "Analiz tamamlandı! Sonuçlar hazır." 
                : result.Result?.ToString() ?? "Analiz tamamlandı";
            
            // Store full result in separate cache
            ResultCache.Set($"analysis_result_{result.RequestId}", result.Result);
            
            logger.LogInformation("Result saved: {RequestId} - {Status}", result.RequestId, result.Status);
        }
        else
        {
            logger.LogWarning("Status not found for RequestId: {RequestId}", result.RequestId);
        }
        
        return Task.FromResult(Results.Ok(new { success = true }));
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

// Result cache for storing full analysis results
internal static class ResultCache
{
    private static readonly Dictionary<string, object?> _cache = new();
    
    public static object? Get(string key) 
        => _cache.TryGetValue(key, out var value) ? value : null;
    
    public static void Set(string key, object? value)
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
/// Progress update from AI service
/// </summary>
public record ProgressUpdate(
    Guid RequestId,
    int Progress,
    string Message,
    DateTime Timestamp
);

/// <summary>
/// Final analysis result from AI service
/// </summary>
public record AnalysisResult(
    Guid RequestId,
    string Status,
    object? Result,
    DateTime CompletedAt
);

/// <summary>
/// AI Analizi başlatma isteği
/// </summary>
public record AIAnalysisRequest(
    string UserId,
    string CompanyId,
    Guid ConnectionId,
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

using Grafirio.Contracts.AI;
using MassTransit;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Features.AI;

/// <summary>
/// .NET â†’ Django AI Bridge Consumer
/// Converts MassTransit messages to Django AI format and forwards via RabbitMQ
/// </summary>
public class DataAnalysisRequestConsumer : IConsumer<IDataAnalysisRequest>
{
    private readonly ILogger<DataAnalysisRequestConsumer> _logger;
    private readonly IPublishEndpoint _publishEndpoint;

    public DataAnalysisRequestConsumer(
        ILogger<DataAnalysisRequestConsumer> logger,
        IPublishEndpoint publishEndpoint)
    {
        _logger = logger;
        _publishEndpoint = publishEndpoint;
    }

    public async Task Consume(ConsumeContext<IDataAnalysisRequest> context)
    {
        var request = context.Message;
        
        _logger.LogInformation(
            "ğŸ¤– .NET â†’ Django AI Bridge | RequestId: {RequestId}, DB: {Database}, Tables: {Count}",
            request.RequestId, request.Database, request.Tables?.Count ?? 0
        );

        try
        {
            await SendToDjangoAI(request);
            
            _logger.LogInformation(
                "âœ… Forwarded to Django AI | RequestId: {RequestId}",
                request.RequestId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "âŒ Django AI forward failed | RequestId: {RequestId}",
                request.RequestId
            );
        }
    }

    private async Task SendToDjangoAI(IDataAnalysisRequest request)
    {
        var message = new
        {
            request_id = request.RequestId.ToString(),
            user_id = request.UserId,
            company_id = request.CompanyId,
            request_time = request.RequestTime.ToString("o"),
            connection_info = new
            {
                host = request.Host,
                port = request.Port,
                database = request.Database,
                username = request.Username,
                password = request.Password,
                trust_server_certificate = request.TrustServerCertificate
            },
            tables = request.Tables,
            settings = new
            {
                sampling_rate = request.SamplingRate,
                null_handling = request.NullHandling,
                data_format = request.DataFormat
            }
        };

        // RabbitMQ direkt connection kullan (Django ile uyumlu)
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest123"
        };        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        // Exchange declare et (Django'nun beklediÄŸi)
        await channel.ExchangeDeclareAsync(
            exchange: "ai.requests",
            type: ExchangeType.Topic,
            durable: true
        );

        // MesajÄ± JSON'a Ã§evir
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        // Publish et
        await channel.BasicPublishAsync(
            exchange: "ai.requests",
            routingKey: "ai.request.graph",
            body: body
        );

        _logger.LogInformation(
            "ğŸ“¤ â†’ Django AI: exchange=ai.requests, routing_key=ai.request.graph, size={Size}bytes",
            body.Length
        );

        await Task.CompletedTask;
    }
}

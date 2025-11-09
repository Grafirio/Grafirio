using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Grafirio.DataAnalysis.Api.Features.Testing;

public static class TestDjangoEndpoints
{
    public static void MapTestDjangoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/test").WithTags("Testing");

        group.MapPost("/django-direct", SendDirectToDjango)
            .WithName("TestDjangoDirectSend")
            .WithOpenApi();
    }

    private static async Task<IResult> SendDirectToDjango(
        TestRequest request,
        ILogger<TestRequest> logger)
    {
        logger.LogInformation("🧪 TEST: Django'ya direkt mesaj gönderiliyor...");
        logger.LogInformation("Database: {Database}, Tables: {Tables}", 
            request.Database, string.Join(", ", request.Tables));

        try
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

            // Django'nun dinlediği exchange
            await channel.ExchangeDeclareAsync(
                exchange: "ai.requests",
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false
            );

            var message = new
            {
                request_id = Guid.NewGuid().ToString(),
                user_id = request.UserId,
                company_id = request.CompanyId,
                connection_info = new
                {
                    host = request.Host,
                    port = request.Port,
                    database = request.Database,
                    username = request.Username,
                    password = request.Password
                },
                tables = request.Tables,
                settings = new
                {
                    sampling_rate = 100,
                    null_handling = "ignore",
                    data_format = "json"
                }
            };

            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            logger.LogInformation("📤 Mesaj gönderiliyor: {MessageSize} bytes", body.Length);
            logger.LogInformation("Exchange: ai.requests, Routing Key: ai.request.graph");

            await channel.BasicPublishAsync(
                exchange: "ai.requests",
                routingKey: "ai.request.graph",
                body: body
            );

            logger.LogInformation("✅ Mesaj başarıyla gönderildi!");

            return Results.Ok(new
            {
                success = true,
                requestId = message.request_id,
                messageSize = body.Length,
                exchange = "ai.requests",
                routingKey = "ai.request.graph"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ RabbitMQ'ya mesaj gönderilirken hata!");
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "RabbitMQ connection failed"
            );
        }
    }
}

public record TestRequest(
    string UserId,
    string CompanyId,
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    List<string> Tables
);

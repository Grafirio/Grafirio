using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Features.AI;

public static class TestDjangoEndpoints
{
    public static void MapTestDjangoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/test/django-direct", SendDirectToDjango)
            .WithName("TestDjangoDirectSend")
            .WithTags("Testing");
    }

    private static async Task<IResult> SendDirectToDjango(
        [FromBody] TestRequest request,
        [FromServices] ILogger<TestRequest> logger)
    {
        try
        {
            logger.LogInformation("🧪 TEST: Sending message directly to Django AI");

            var message = new
            {
                request_id = Guid.NewGuid().ToString(),
                user_id = request.UserId,
                company_id = request.CompanyId,
                request_time = DateTime.UtcNow.ToString("o"),
                connection_info = new
                {
                    host = request.Host,
                    port = request.Port,
                    database = request.Database,
                    username = request.Username,
                    password = request.Password,
                    trust_server_certificate = true
                },
                tables = request.Tables,
                settings = new
                {
                    sampling_rate = 100,
                    null_handling = "keep",
                    data_format = "json"
                }
            };

            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "guest",
                Password = "guest123"
            };

            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();

            // Exchange declare
            await channel.ExchangeDeclareAsync(
                exchange: "ai.requests",
                type: ExchangeType.Topic,
                durable: true
            );

            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            await channel.BasicPublishAsync(
                exchange: "ai.requests",
                routingKey: "ai.request.graph",
                body: body
            );

            logger.LogInformation("✅ Message sent: {Size} bytes", body.Length);

            return Results.Ok(new
            {
                success = true,
                message = "Mesaj Django AI'ya gönderildi",
                messageSize = body.Length,
                messagePreview = json.Substring(0, Math.Min(200, json.Length))
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Test failed");
            return Results.BadRequest(new { success = false, error = ex.Message });
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

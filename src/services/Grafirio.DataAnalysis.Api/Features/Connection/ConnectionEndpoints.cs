using Grafirio.DataAnalysis.Api.Models;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Features.Connection;

public static class ConnectionEndpoints
{
    public static void MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connection")
            .WithTags("Connection Management")
            .WithOpenApi();

        group.MapPost("/test", TestConnection)
            .WithName("TestConnection")
            .WithDescription("Test SQL Server connection with provided credentials");
    }

    private static async Task<IResult> TestConnection(SqlConnectionRequest request)
    {
        try
        {
            var connectionString = BuildConnectionString(request);
            
            // Debug: Log connection string (şifre hariç)
            Console.WriteLine($"[DEBUG] Attempting connection to: Server={request.Host},{request.Port}; Database={request.Database}; User={request.Username}");
            
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            // Bağlantı başarılı, benzersiz bir ID oluştur
            var connectionId = Guid.NewGuid().ToString();
            
            // Connection string can be cached in Redis with TTL
            // await _redis.SetAsync($"conn:{connectionId}", connectionString, TimeSpan.FromHours(1));
            
            return Results.Ok(new TestConnectionResponse(
                Success: true,
                Message: "Connection successful",
                ConnectionId: connectionId
            ));
        }
        catch (SqlException ex)
        {
            Console.WriteLine($"[ERROR] SQL Exception: {ex.Message}");
            Console.WriteLine($"[ERROR] Error Number: {ex.Number}");
            Console.WriteLine($"[ERROR] Error State: {ex.State}");
            
            return Results.Ok(new TestConnectionResponse(
                Success: false,
                Message: $"SQL Error: {ex.Message} (Error Number: {ex.Number})"
            ));
        }
        catch (Exception ex)
        {
            return Results.Ok(new TestConnectionResponse(
                Success: false,
                Message: $"Connection failed: {ex.Message}"
            ));
        }
    }

    internal static string BuildConnectionString(SqlConnectionRequest request)
    {
        var builder = new SqlConnectionStringBuilder
        {
            // tcp: prefix ile Named Pipes yerine TCP zorla (Docker container'lar için gerekli)
            DataSource = $"tcp:{request.Host},{request.Port}",
            InitialCatalog = request.Database,
            UserID = request.Username,
            Password = request.Password,
            IntegratedSecurity = false,  // SQL Server Authentication kullan
            TrustServerCertificate = request.TrustServerCertificate,
            ConnectTimeout = 10,
            Encrypt = true,
            MultipleActiveResultSets = true,
            Pooling = true
        };

        return builder.ConnectionString;
    }
}

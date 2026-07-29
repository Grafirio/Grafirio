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

    /// <summary>
    /// Host alanına "sunucu,1433" veya "sunucu:1433" biçiminde port yapıştırmak yaygın;
    /// ayrı Port alanıyla birleşince "tcp:sunucu,1433,1433" gibi geçersiz bir adres çıkıyordu.
    /// Host'a gömülü portu ayıklayıp, ayrı bir port verilmemişse onu kullan.
    /// </summary>
    internal static (string Host, int Port) NormalizeHostAndPort(string? host, int port)
    {
        var trimmed = (host ?? string.Empty).Trim();
        var separator = trimmed.LastIndexOfAny(new[] { ',', ':' });

        if (separator > 0 && int.TryParse(trimmed[(separator + 1)..].Trim(), out var embeddedPort))
        {
            var bareHost = trimmed[..separator].Trim();
            // IPv6 adreslerinde ':' zaten adresin parçası — yalnızca tek ayraç varsa güvenli.
            if (bareHost.Length > 0 && !bareHost.Contains(':'))
            {
                return (bareHost, port > 0 ? port : embeddedPort);
            }
        }

        return (trimmed, port > 0 ? port : 1433);
    }

    internal static string BuildConnectionString(SqlConnectionRequest request)
    {
        var (host, port) = NormalizeHostAndPort(request.Host, request.Port);

        var builder = new SqlConnectionStringBuilder
        {
            // tcp: prefix ile Named Pipes yerine TCP zorla (Docker container'lar için gerekli)
            DataSource = $"tcp:{host},{port}",
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

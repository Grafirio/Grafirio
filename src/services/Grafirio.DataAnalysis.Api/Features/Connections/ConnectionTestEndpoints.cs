using Grafirio.DataAnalysis.Api.Models;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

/// <summary>
/// Baglanti denemesi: kullanicinin girdigi bilgilerle gercekten baglanilabiliyor
/// mu diye bakar. Kayitli baglantilarin CRUD'u <see cref="ConnectionEndpoints"/>'te.
///
/// Onceden bu sinif da <c>ConnectionEndpoints</c> adiyla ayri bir
/// <c>Features.Connection</c> (tekil) ad alanindaydi. Iki ayni adli sinif ve
/// bir harf farkli iki klasor, hangi dosyanin hangi ucu kurdugunu okunamaz
/// yapiyordu; ayrica baglanti dizesini kuran yardimcilar da burada oldugu icin
/// "endpoint dosyasi" olmayan yerlerden cagriliyordu.
/// </summary>
public static class ConnectionTestEndpoints
{
    public static void MapConnectionTestEndpoints(this IEndpointRouteBuilder app)
    {
        // Kimlik dogrulamasi zorunlu. Uc daha once aciktı: istekteki host'a
        // sunucu adina baglanti kuruyordu, yani kimligi olmayan biri bunu
        // ic aglari yoklamak icin kullanabilirdi.
        var group = app.MapGroup("/api/connections")
            .RequireAuthorization("CompanyAccess")
            .WithTags("Connection Management")
            .WithOpenApi();

        group.MapPost("/test", TestConnection)
            .WithName("TestConnection")
            .WithDescription("Verilen bilgilerle SQL Server bağlantısını dener");
    }

    private static async Task<IResult> TestConnection(
        SqlConnectionRequest request,
        ILogger<SqlConnectionRequest> logger)
    {
        try
        {
            var connectionString = BuildConnectionString(request);

            logger.LogInformation(
                "Bağlantı deneniyor: {Host},{Port} / {Database} / {User}",
                request.Host, request.Port, request.Database, request.Username);

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            return Results.Ok(new TestConnectionResponse(
                Success: true,
                Message: "Bağlantı başarılı",
                ConnectionId: Guid.NewGuid().ToString()
            ));
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "SQL bağlantı hatası. Numara: {Number}", ex.Number);

            return Results.Ok(new TestConnectionResponse(
                Success: false,
                Message: $"SQL hatası: {ex.Message} (hata no: {ex.Number})"
            ));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bağlantı kurulamadı");

            return Results.Ok(new TestConnectionResponse(
                Success: false,
                Message: $"Bağlantı kurulamadı: {ex.Message}"
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
        var separator = trimmed.LastIndexOfAny([',', ':']);

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

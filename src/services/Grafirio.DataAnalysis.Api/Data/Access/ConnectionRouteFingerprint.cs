using System.Security.Cryptography;
using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data.Entities;

namespace Grafirio.DataAnalysis.Api.Data.Access;

public static class ConnectionRouteFingerprint
{
    private const long TicksPerMicrosecond = 10;

    public static string Create(SavedConnection connection) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            connection.Id, connection.CompanyId, connection.UserId, connection.Name,
            connection.Host, connection.Port, connection.Database, connection.Username,
            connection.EncryptedPassword, connection.TrustServerCertificate, connection.IsActive,
            CreatedAt = connection.CreatedAt.Ticks / TicksPerMicrosecond,
            UpdatedAt = connection.UpdatedAt?.Ticks / TicksPerMicrosecond
        })));

    public static DateTime NextUpdatedAt(SavedConnection connection)
    {
        // PostgreSQL timestamps round-trip at microsecond precision, not .NET tick precision.
        var ticks = DateTime.UtcNow.Ticks / TicksPerMicrosecond * TicksPerMicrosecond;
        var previous = (connection.UpdatedAt ?? connection.CreatedAt).Ticks;
        return new DateTime(Math.Max(ticks,
            previous / TicksPerMicrosecond * TicksPerMicrosecond + TicksPerMicrosecond), DateTimeKind.Utc);
    }
}
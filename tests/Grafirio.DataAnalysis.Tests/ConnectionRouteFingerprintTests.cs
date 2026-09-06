using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;

namespace Grafirio.DataAnalysis.Tests;

public sealed class ConnectionRouteFingerprintTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("company")]
    [InlineData("user")]
    [InlineData("name")]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("database")]
    [InlineData("username")]
    [InlineData("ciphertext")]
    [InlineData("trust")]
    [InlineData("active")]
    [InlineData("created")]
    [InlineData("updated")]
    public void BindingCoversIdentityTargetEncryptedCredentialsAndRevision(string field)
    {
        var connection = new SavedConnection();
        var fingerprint = ConnectionRouteFingerprint.Create(connection);
        switch (field)
        {
            case "id": connection.Id = Guid.NewGuid(); break;
            case "company": connection.CompanyId = "company"; break;
            case "user": connection.UserId = "user"; break;
            case "name": connection.Name = "name"; break;
            case "host": connection.Host = "host"; break;
            case "port": connection.Port = 1433; break;
            case "database": connection.Database = "database"; break;
            case "username": connection.Username = "reader"; break;
            case "ciphertext": connection.EncryptedPassword = "not-plaintext-or-even-valid-ciphertext"; break;
            case "trust": connection.TrustServerCertificate = false; break;
            case "active": connection.IsActive = false; break;
            case "created": connection.CreatedAt = DateTime.UtcNow; break;
            case "updated": connection.UpdatedAt = DateTime.UtcNow; break;
        }
        Assert.NotEqual(fingerprint, ConnectionRouteFingerprint.Create(connection));
    }

    [Fact]
    public void TimestampPrecisionMatchesPostgresAndRouteOnlyChangesAdvanceVersion()
    {
        var connection = new SavedConnection { CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var fingerprint = ConnectionRouteFingerprint.Create(connection);
        connection.CreatedAt = connection.CreatedAt.AddTicks(-(connection.CreatedAt.Ticks % 10));
        connection.UpdatedAt = connection.UpdatedAt.Value.AddTicks(-(connection.UpdatedAt.Value.Ticks % 10));
        Assert.Equal(fingerprint, ConnectionRouteFingerprint.Create(connection));
        connection.UpdatedAt = ConnectionRouteFingerprint.NextUpdatedAt(connection);
        Assert.NotEqual(fingerprint, ConnectionRouteFingerprint.Create(connection));
        var revision = connection.UpdatedAt;
        connection.UpdatedAt = ConnectionRouteFingerprint.NextUpdatedAt(connection);
        Assert.True(connection.UpdatedAt > revision);
    }
}
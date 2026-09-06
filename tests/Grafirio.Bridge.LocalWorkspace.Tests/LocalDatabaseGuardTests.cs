using Grafirio.Bridge.Desktop.LocalWorkspace.Data;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using Grafirio.QueryPolicy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Grafirio.Bridge.LocalWorkspace.Tests;

public sealed class LocalDatabaseGuardTests
{
    private static readonly Guid ConnectionId = Guid.Parse("be051c81-4fad-4f45-8a04-676fd02361a4");

    [Theory]
    [InlineData("DELETE FROM dbo.Sales")]
    [InlineData("SELECT NEXT VALUE FOR dbo.Counter")]
    [InlineData("SELECT dbo.SideEffect()")]
    public async Task NonReadOnlySqlIsRejectedWithoutDatabaseContact(string sql)
    {
        var database = new LocalDatabase(NullLogger<LocalDatabase>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => database.QueryAsync(
            Connection(), Request(sql), CancellationToken.None));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Secrets")]
    [InlineData("SELECT * FROM Sales")]
    [InlineData("SELECT * FROM dbo.Sales, dbo.Secrets")]
    [InlineData("WITH x AS (SELECT * FROM dbo.Secrets) SELECT * FROM x")]
    public async Task TableScopeIsEnforcedBeforeOpeningConnection(string sql)
    {
        var database = new LocalDatabase(NullLogger<LocalDatabase>.Instance);
        await Assert.ThrowsAsync<QueryPolicyException>(() => database.QueryAsync(
            Connection(), Request(sql), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyAllowlistDeniesAllCustomerTables()
    {
        var database = new LocalDatabase(NullLogger<LocalDatabase>.Instance);
        await Assert.ThrowsAsync<QueryPolicyException>(() => database.QueryAsync(
            Connection() with { AllowedTables = [] }, Request("SELECT * FROM dbo.Sales"), CancellationToken.None));
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    public async Task UnsupportedDialectNeverFallsBackToUnguardedSql(string provider)
    {
        var database = new LocalDatabase(NullLogger<LocalDatabase>.Instance);
        await Assert.ThrowsAsync<NotSupportedException>(() => database.QueryAsync(
            Connection() with { Provider = provider }, Request("SELECT 1"), CancellationToken.None));
    }

    private static LocalQueryRequest Request(string sql) => new(ConnectionId, sql, 100, 1);

    private static LocalConnection Connection() => new()
    {
        Id = ConnectionId, Host = "unreachable.invalid", Name = "Guard test", Database = "Sample",
        Username = "readonly", Password = "sample-only", AllowedTables = ["dbo.Sales"]
    };
}
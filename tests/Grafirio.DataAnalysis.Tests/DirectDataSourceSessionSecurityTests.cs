using System.Reflection;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.QueryPolicy;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Tests;

public class DirectDataSourceSessionSecurityTests
{
    [Theory]
    [InlineData("DELETE FROM dbo.Allowed")]
    [InlineData("SELECT * FROM dbo.Secret")]
    [InlineData("SELECT NEXT VALUE FOR dbo.Sequence")]
    [InlineData("SELECT * FROM dbo.Allowed, dbo.Secret")]
    public async Task EveryOperationValidatesBeforeUsingConnection(string sql)
    {
        // A closed, unconfigured connection makes any accidental execution fail differently.
        await using var session = (DirectDataSourceSession)Activator.CreateInstance(
            typeof(DirectDataSourceSession), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [new SqlConnection(), new[] { "dbo.Allowed" }], culture: null)!;

        await Assert.ThrowsAsync<QueryPolicyException>(() => session.QueryAsync<int>(sql));
        await Assert.ThrowsAsync<QueryPolicyException>(() => session.QueryRowsAsync(sql));
        await Assert.ThrowsAsync<QueryPolicyException>(() => session.ScalarAsync<int>(sql));
        await Assert.ThrowsAsync<QueryPolicyException>(async () =>
        {
            await foreach (var row in session.StreamAsync(sql))
                Assert.Fail("No rows may be read for rejected SQL.");
        });
    }
}
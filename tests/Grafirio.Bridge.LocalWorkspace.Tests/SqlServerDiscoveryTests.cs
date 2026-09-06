using Grafirio.Bridge.Desktop.LocalWorkspace.Data;
using Grafirio.QueryPolicy;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Grafirio.Bridge.LocalWorkspace.Tests;

public sealed class SqlServerDiscoveryTests
{
    [Fact]
    public void EmptyAllowlistDeniesAllMetadataRows()
    {
        using var command = new SqlCommand();
        SqlServerDiscovery.Configure(command, []);
        Assert.Contains("AND (1 = 0)", command.CommandText);
        Assert.Empty(command.Parameters.Cast<SqlParameter>());
    }

    [Fact]
    public void OnlyExactSchemaQualifiedAllowlistValuesBecomeParameters()
    {
        using var command = new SqlCommand();
        SqlServerDiscovery.Configure(command, ["dbo.Sales", "[audit].[Sales]", "dbo.Sales"]);
        Assert.Equal(4, command.Parameters.Count);
        Assert.Equal("dbo", command.Parameters["@schema0"].Value);
        Assert.Equal("Sales", command.Parameters["@table0"].Value);
        Assert.Equal("audit", command.Parameters["@schema1"].Value);
        Assert.DoesNotContain("dbo", command.CommandText);
        Assert.DoesNotContain("Sales", command.CommandText);
        Assert.Contains("COLLATE Latin1_General_100_BIN2", command.CommandText);
        Assert.Contains("DATALENGTH(c.TABLE_NAME)", command.CommandText);
        Assert.Contains("t.TABLE_TYPE = 'BASE TABLE'", command.CommandText);
    }

    [Theory]
    [InlineData("Sales")]
    [InlineData("dbo.Sales; SELECT * FROM dbo.Secrets")]
    [InlineData("dbo.Sales WHERE 1=1")]
    public void AllowlistCannotInjectMetadataSql(string table)
    {
        using var command = new SqlCommand();
        Assert.Throws<QueryPolicyException>(() => SqlServerDiscovery.Configure(command, [table]));
    }

    [Fact]
    public void QuotedIdentifiersAreDecodedButNeverInterpolated()
    {
        using var command = new SqlCommand();
        SqlServerDiscovery.Configure(command, ["[my schema].[Sales; 'quoted']"]);
        Assert.Equal("my schema", command.Parameters["@schema0"].Value);
        Assert.Equal("Sales; 'quoted'", command.Parameters["@table0"].Value);
        Assert.DoesNotContain("quoted", command.CommandText);
    }
}
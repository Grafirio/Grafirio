using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.QueryPolicy.Tests;

public class AggregatePolicyTests
{
    [Fact]
    public void DistinctCountPassesValidation()
    {
        const string sql = "SELECT COUNT(DISTINCT [t0].[CustomerId]) AS [value] "
                           + "FROM [dbo].[Orders] AS [t0]";

        var result = SqlPolicy.Validate(sql, ["dbo.Orders"]);

        Assert.Equal("[dbo].[Orders]", Assert.Single(result.Tables).CanonicalName);
    }

    [Theory]
    [InlineData(
        "SELECT DISTINCT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY [t0].[Amount]) "
        + "OVER () AS [value] FROM [dbo].[Calculations] AS [t0]")]
    [InlineData(
        "SELECT DISTINCT TOP (5) [t0].[Category], PERCENTILE_CONT(0.9) "
        + "WITHIN GROUP (ORDER BY [t0].[Amount]) OVER (PARTITION BY [t0].[Category]) "
        + "AS [value] FROM [dbo].[Calculations] AS [t0] "
        + "ORDER BY [value] DESC, [t0].[Category] ASC")]
    public void PercentileAggregationPassesValidation(string sql)
    {
        var result = SqlPolicy.Validate(sql, ["dbo.Calculations"]);

        Assert.Equal("[dbo].[Calculations]", Assert.Single(result.Tables).CanonicalName);
    }
}

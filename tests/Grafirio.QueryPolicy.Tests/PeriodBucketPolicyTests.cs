using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.QueryPolicy.Tests;

/// <summary>
/// PyCaret motorunun donem yuvarlama (PeriodBucket) SQL kaliplari paylasilan
/// salt-okuma politikasindan gecmek zorunda. Bu sozlesme burada sabitleniyor:
/// motor tarafinda uretim sekli degisir de politika reddederse hata derlemede
/// degil, kullanicinin sorusu calisirken ortaya cikar.
/// </summary>
public class PeriodBucketPolicyTests
{
    [Theory]
    [InlineData("CONVERT(char(4), [t0].[CreatedDate], 126)")]
    [InlineData("CONVERT(char(7), [t0].[CreatedDate], 126)")]
    [InlineData("CONVERT(char(10), [t0].[CreatedDate], 126)")]
    [InlineData("CONCAT(YEAR([t0].[CreatedDate]), '-Q', (MONTH([t0].[CreatedDate]) + 2) / 3)")]
    [InlineData("CONCAT(MONTH([t0].[CreatedDate]) / 10, MONTH([t0].[CreatedDate]) % 10)")]
    [InlineData("CONCAT('Q', (MONTH([t0].[CreatedDate]) + 2) / 3)")]
    public void PeriodBucketedSeriesPassesValidation(string bucket)
    {
        var sql = $"SELECT TOP (500) {bucket} AS [Donem], SUM([t0].[Amount]) AS [value] "
                  + "FROM [dbo].[Calculations] AS [t0] "
                  + "WHERE [t0].[CreatedDate] IS NOT NULL AND [t0].[CreatedDate] >= @p0 "
                  + $"GROUP BY {bucket} ORDER BY {bucket} ASC";

        var result = SqlPolicy.Validate(sql, ["dbo.Calculations"]);

        Assert.Equal("[dbo].[Calculations]", Assert.Single(result.Tables).CanonicalName);
    }

    [Fact]
    public void PeriodBucketedGroupCountPassesValidation()
    {
        const string bucket = "CONVERT(char(7), [t0].[CreatedDate], 126)";
        var sql = $"SELECT COUNT(*) FROM (SELECT {bucket} AS [Donem] "
                  + "FROM [dbo].[Calculations] AS [t0] "
                  + $"GROUP BY {bucket}) AS g";

        var result = SqlPolicy.Validate(sql, ["dbo.Calculations"]);

        Assert.Equal("[dbo].[Calculations]", Assert.Single(result.Tables).CanonicalName);
    }

    [Fact]
    public void DistinctCountShapePassesValidation()
    {
        // "Kac farkli musteri" — motor COUNT(DISTINCT ...) uretir; hem olcu
        // hem HAVING konumunda politikadan gecmek zorunda.
        var sql = "SELECT TOP (10) [t0].[Country], "
                  + "COUNT(DISTINCT [t0].[ReferenceId]) AS [value] "
                  + "FROM [dbo].[Calculations] AS [t0] "
                  + "GROUP BY [t0].[Country] "
                  + "HAVING COUNT(DISTINCT [t0].[ReferenceId]) > @h0 "
                  + "ORDER BY [value] DESC, [t0].[Country] ASC";

        var result = SqlPolicy.Validate(sql, ["dbo.Calculations"]);

        Assert.Equal("[dbo].[Calculations]", Assert.Single(result.Tables).CanonicalName);
    }

    [Fact]
    public void YearComparisonShapePassesValidation()
    {
        // period_compare: eksen takvim konumu, seri yil — ikisi birlikte
        // gruplanir ve siralanir. Motorun urettigi bicimin birebir karsiligi.
        const string axis = "CONCAT(MONTH([t0].[CreatedDate]) / 10, MONTH([t0].[CreatedDate]) % 10)";
        const string series = "CONVERT(char(4), [t0].[CreatedDate], 126)";
        var sql = $"SELECT TOP (500) {axis} AS [Donem], {series} AS [Seri], "
                  + "SUM([t0].[Amount]) AS [value] "
                  + "FROM [dbo].[Calculations] AS [t0] "
                  + "WHERE [t0].[CreatedDate] IS NOT NULL AND [t0].[CreatedDate] >= @p0 "
                  + $"GROUP BY {axis}, {series} "
                  + $"ORDER BY {series} ASC, {axis} ASC";

        var result = SqlPolicy.Validate(sql, ["dbo.Calculations"]);

        Assert.Equal("[dbo].[Calculations]", Assert.Single(result.Tables).CanonicalName);
    }
}

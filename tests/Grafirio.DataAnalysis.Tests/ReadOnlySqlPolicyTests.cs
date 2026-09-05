using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.QueryPolicy;

namespace Grafirio.DataAnalysis.Tests;

public class ReadOnlySqlPolicyTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("/* outer /* nested */ comment */ SELECT 'DELETE; EXEC' AS [INTO]; -- end")]
    [InlineData(";WITH x AS (SELECT Id FROM dbo.Allowed) SELECT * FROM x;")]
    [InlineData("WITH x AS (SELECT Id FROM dbo.Allowed), y AS (SELECT Id FROM x) SELECT * FROM y")]
    [InlineData("SELECT a.Id FROM dbo.Allowed a, dbo.Other b WHERE a.Id = b.Id")]
    [InlineData("SELECT * FROM (SELECT Id FROM dbo.Allowed UNION ALL SELECT Id FROM dbo.Other) x")]
    [InlineData("SELECT SUM(SUM(Id)) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM dbo.Allowed GROUP BY Id")]
    [InlineData("SELECT TOP 100 [Id] FROM [dbo].[Allowed] TABLESAMPLE SYSTEM (10 PERCENT)")]
    [InlineData("SELECT CAST(CASE WHEN EXISTS (SELECT Id FROM dbo.Allowed GROUP BY Id HAVING COUNT(*) > 1) THEN 0 ELSE 1 END AS bit)")]
    [InlineData("SELECT Id FROM dbo.Allowed WHERE Id IN @Names AND Id NOT IN @Other")]
    [InlineData("SELECT Id FROM dbo.Allowed WHERE Id IN /* note */ @Names")]
    public void AcceptsSupportedSelects(string sql) =>
        ReadOnlySqlPolicy.Validate(sql, ["dbo.Allowed", "dbo.Other"]);

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1\nGO\nSELECT 2")]
    [InlineData("SELECT 1\nGO")]
    [InlineData("SELECT 1\nGO 5")]
    [InlineData("WITH x AS (SELECT Id FROM dbo.Allowed) UPDATE x SET Id = 1")]
    [InlineData("WITH x AS (SELECT Id FROM dbo.Allowed) DELETE FROM x")]
    [InlineData("SELECT Id INTO /* hidden */ dbo.NewTable FROM dbo.Allowed")]
    [InlineData("SELECT NEXT VALUE FOR dbo.Sequence")]
    [InlineData("SELECT @value = Id FROM dbo.Allowed")]
    [InlineData("DECLARE @x int; SELECT @x")]
    [InlineData("SELECT dbo.SideEffect(Id) FROM dbo.Allowed")]
    [InlineData("SELECT OBJECT_DEFINITION(1)")]
    [InlineData("SELECT SUSER_SNAME()")]
    [InlineData("SELECT * FROM OPENROWSET(BULK 'C:\\secret', SINGLE_CLOB) x")]
    [InlineData("SELECT * FROM OPENQUERY(Remote, 'SELECT 1')")]
    [InlineData("SELECT * FROM Remote.Database.dbo.Allowed")]
    [InlineData("SELECT * FROM Database.dbo.Allowed")]
    [InlineData("SELECT * FROM dbo.Reader()")]
    [InlineData("SELECT * FROM dbo.Allowed WITH (UPDLOCK)")]
    [InlineData("SELECT * FROM dbo.Allowed WITH (NOLOCK)")]
    [InlineData("SELECT * FROM dbo.Allowed OPTION (RECOMPILE)")]
    [InlineData("SELECT * FROM @table")]
    [InlineData("SELECT * FROM #temporary")]
    [InlineData("SELECT * FROM dbo.Allowed WHERE Id IN @Names; EXEC('DELETE FROM dbo.Allowed')")]
    [InlineData("SELECT * FROM dbo.Allowed FOR XML AUTO")]
    [InlineData("SELECT /* unclosed")]
    [InlineData("SELECT 1 GARBAGE GARBAGE")]
    public void RejectsUnsafeOrUnsupportedSql(string sql) =>
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(sql, ["dbo.Allowed"]));

    [Theory]
    [InlineData("SELECT * FROM dbo.Allowed, dbo.Secret")]
    [InlineData("SELECT * FROM dbo.Allowed WHERE Id IN (SELECT Id FROM dbo.Secret)")]
    [InlineData("SELECT * FROM dbo.Allowed UNION SELECT * FROM dbo.Secret")]
    [InlineData("WITH x AS (SELECT * FROM dbo.Secret) SELECT * FROM x")]
    [InlineData("WITH Secret AS (SELECT * FROM dbo.Allowed) SELECT * FROM dbo.Secret")]
    [InlineData("WITH x AS (SELECT * FROM Secret), Secret AS (SELECT * FROM dbo.Allowed) SELECT * FROM x")]
    [InlineData("SELECT * FROM Allowed")]
    [InlineData("SELECT * FROM dbo.allowed")]
    public void RejectsUnauthorizedTablesAcrossAllScopes(string sql) =>
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(sql, ["dbo.Allowed"]));

    [Fact]
    public void EmptyAndNullAllowlistsDenyCustomerTables()
    {
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate("SELECT * FROM dbo.Allowed"));
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate("SELECT * FROM dbo.Allowed", []));
        ReadOnlySqlPolicy.Validate("SELECT 1");
    }

    [Fact]
    public void MetadataRequiresOptInAndExactCatalogIdentities()
    {
        const string sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES";
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(sql));
        ReadOnlySqlPolicy.Validate(sql, allowMetadata: true);
        Assert.Throws<QueryPolicyException>(() =>
            ReadOnlySqlPolicy.Validate("SELECT * FROM sys.sql_logins", allowMetadata: true));
        Assert.Throws<QueryPolicyException>(() =>
            ReadOnlySqlPolicy.Validate("SELECT * FROM sys.dm_exec_sessions", ["sys.dm_exec_sessions"], true));
        Assert.Throws<QueryPolicyException>(() =>
            ReadOnlySqlPolicy.Validate("SELECT * FROM sys.tables, dbo.Secret", allowMetadata: true));
    }

    [Fact]
    public void ProfilingCatalogQueriesSupportDapperLists()
    {
        ReadOnlySqlPolicy.Validate(
            "SELECT OBJECT_SCHEMA_NAME(fk.parent_object_id) FROM sys.foreign_keys fk", allowMetadata: true);
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(
            "SELECT OBJECT_SCHEMA_NAME(1, 2)", allowMetadata: true));
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate("SELECT OBJECT_SCHEMA_NAME(1)"));
        ReadOnlySqlPolicy.Validate("""
            SELECT s.name + '.' + t.name AS TableName, MIN(c.name) AS ColumnName
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE (i.is_primary_key = 1 OR i.is_unique = 1)
              AND s.name + '.' + t.name IN @Names
            GROUP BY s.name, t.name, i.object_id, i.index_id HAVING COUNT(*) = 1
            """, allowMetadata: true);
    }

    [Fact]
    public void CanonicalIdentityPreservesEscapedBracketsDotsAndSpaces()
    {
        var result = ReadOnlySqlPolicy.Validate(
            "SELECT * FROM [odd.schema].[table]] with.dot]", ["[odd.schema].[table]] with.dot]"]);
        var table = Assert.Single(result.Tables);
        Assert.Equal("odd.schema", table.Schema);
        Assert.Equal("table] with.dot", table.Name);
        Assert.Equal("[odd.schema].[table]] with.dot]", table.CanonicalName);
        Assert.Throws<QueryPolicyException>(() =>
            ReadOnlySqlPolicy.Validate("SELECT * FROM [a.b].[c]", ["[a].[b.c]"]));
    }

    [Theory]
    [InlineData("dbo.Allowed; DROP TABLE dbo.Allowed")]
    [InlineData("dbo.Allowed a")]
    [InlineData("dbo.Allowed WHERE 1=1")]
    [InlineData("dbo.Allowed -- comment")]
    [InlineData("Allowed")]
    [InlineData("db.dbo.Allowed")]
    public void RejectsMalformedAllowlistEntries(string entry) =>
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate("SELECT 1", [entry]));
}
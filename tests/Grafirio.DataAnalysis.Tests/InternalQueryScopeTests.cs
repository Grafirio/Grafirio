using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.QueryPolicy;

namespace Grafirio.DataAnalysis.Tests;

public sealed class InternalQueryScopeTests
{
    private const string CompanyId = "company-a";
    private const string ConfigJson = "{\"label\":\"İstanbul\"}";
    private static readonly Guid QueryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ConfigId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ConnectionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void BoundProcessingQueryReturnsConfiguredTables()
    {
        var (query, config, connection) = CreateScope();
        var tables = Validate(query, config, connection);
        Assert.Equal(new[] { "dbo.Orders" }, tables);
    }

    [Theory]
    [InlineData("missing-query")]
    [InlineData("missing-config")]
    [InlineData("missing-connection")]
    [InlineData("query-id")]
    [InlineData("query-config")]
    [InlineData("query-pending")]
    [InlineData("query-completed")]
    [InlineData("query-failed")]
    [InlineData("config-id")]
    [InlineData("config-company")]
    [InlineData("config-connection")]
    [InlineData("config-inactive")]
    [InlineData("config-pending")]
    [InlineData("config-hash")]
    [InlineData("connection-id")]
    [InlineData("connection-company")]
    [InlineData("connection-inactive")]
    public void RejectsUnboundOrInactivePersistedState(string change)
    {
        var (query, config, connection) = CreateScope();
        switch (change)
        {
            case "missing-query": query = null; break;
            case "missing-config": config = null; break;
            case "missing-connection": connection = null; break;
            case "query-id": query.Id = Guid.NewGuid(); break;
            case "query-config": query.ConfigId = Guid.NewGuid(); break;
            case "query-pending": query.Status = "pending"; break;
            case "query-completed": query.Status = "completed"; break;
            case "query-failed": query.Status = "failed"; break;
            case "config-id": config.Id = Guid.NewGuid(); break;
            case "config-company": config.CompanyId = "company-b"; break;
            case "config-connection": config.ConnectionId = Guid.NewGuid(); break;
            case "config-inactive": config.IsActive = false; break;
            case "config-pending": config.Status = "pending"; break;
            case "config-hash": config.ConfigJson += " "; break;
            case "connection-id": connection.Id = Guid.NewGuid(); break;
            case "connection-company": connection.CompanyId = "company-b"; break;
            case "connection-inactive": connection.IsActive = false; break;
        }

        Assert.Throws<QueryPolicyException>(() => Validate(query, config, connection));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public void RejectsInvalidHash(string? hash)
    {
        var (query, config, connection) = CreateScope();
        Assert.Throws<QueryPolicyException>(() => InternalQueryScope.Validate(
            CompanyId, QueryId, ConfigId, ConnectionId, hash, query, config, connection, ["dbo.Orders"]));
    }

    [Theory]
    [InlineData("company-b")]
    [InlineData("")]
    [InlineData(null)]
    public void CompanyClaimAloneCannotAuthorizeTheQuery(string? companyId)
    {
        var (query, config, connection) = CreateScope();
        Assert.Throws<QueryPolicyException>(() => InternalQueryScope.Validate(
            companyId, QueryId, ConfigId, ConnectionId, InternalQueryScope.ComputeConfigHash(ConfigJson),
            query, config, connection, ["dbo.Orders"]));
    }

    [Fact]
    public void HashUsesExactUtf8BytesWithoutJsonNormalization()
    {
        Assert.Equal("44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
            InternalQueryScope.ComputeConfigHash("{}"));
        Assert.NotEqual(InternalQueryScope.ComputeConfigHash(ConfigJson),
            InternalQueryScope.ComputeConfigHash(ConfigJson + " "));
        Assert.NotEqual(InternalQueryScope.ComputeConfigHash(ConfigJson),
            InternalQueryScope.ComputeConfigHash("{\"label\":\"\\u0130stanbul\"}"));
    }

    [Fact]
    public void ChangedSelectionBlocksAnOtherwiseValidQuery()
    {
        var (query, config, connection) = CreateScope();
        Assert.Throws<QueryPolicyException>(() => InternalQueryScope.Validate(
            CompanyId, QueryId, ConfigId, ConnectionId, InternalQueryScope.ComputeConfigHash(ConfigJson),
            query, config, connection, ["dbo.Orders", "dbo.Customers"]));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Customers")]
    [InlineData("SELECT * FROM dbo.Orders WHERE EXISTS (SELECT 1 FROM dbo.Customers)")]
    [InlineData("WITH hidden AS (SELECT * FROM dbo.Customers) SELECT * FROM hidden")]
    [InlineData("SELECT * FROM dbo.Orders UNION ALL SELECT * FROM dbo.Customers")]
    [InlineData("SELECT * FROM sys.sql_modules")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.COLUMNS CROSS JOIN dbo.Customers")]
    public void ValidJobDoesNotPermitUnknownTablesAnywhereInSql(string sql)
    {
        var (query, config, connection) = CreateScope();
        var allowed = Validate(query, config, connection);
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(sql, allowed, allowMetadata: true));
    }

    [Fact]
    public void AllowsParameterizedColumnCatalogQueryForValidScope()
    {
        var (query, config, connection) = CreateScope();
        var allowed = Validate(query, config, connection);
        ReadOnlySqlPolicy.Validate("""
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @TableName
            """, allowed, allowMetadata: true);
    }

    private static IReadOnlyList<string> Validate(
        QueryHistory? query, AnalysisConfig? config, SavedConnection? connection) =>
        InternalQueryScope.Validate(CompanyId, QueryId, ConfigId, ConnectionId,
            InternalQueryScope.ComputeConfigHash(ConfigJson), query, config, connection, ["dbo.Orders"]);

    private static (QueryHistory Query, AnalysisConfig Config, SavedConnection Connection) CreateScope() =>
        (new QueryHistory { Id = QueryId, ConfigId = ConfigId, Status = "processing" },
         new AnalysisConfig
         {
             Id = ConfigId, CompanyId = CompanyId, ConnectionId = ConnectionId,
             Status = "ready", IsActive = true, ConfigJson = ConfigJson, TablesJson = "[\"dbo.Orders\"]"
         },
         new SavedConnection { Id = ConnectionId, CompanyId = CompanyId, IsActive = true });
}
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using Grafirio.QueryPolicy;
using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Data;

public sealed class LocalDatabase(ILogger<LocalDatabase> logger) : ILocalDatabase
{
    private const string DiscoverySql = """
        SELECT c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE
        FROM INFORMATION_SCHEMA.COLUMNS c
        INNER JOIN INFORMATION_SCHEMA.TABLES t
            ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
        WHERE t.TABLE_TYPE = 'BASE TABLE'
        ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
        """;
    private const string MySqlDiscoverySql = """
        SELECT c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE
        FROM INFORMATION_SCHEMA.COLUMNS c
        INNER JOIN INFORMATION_SCHEMA.TABLES t
            ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
        WHERE t.TABLE_TYPE = 'BASE TABLE' AND c.TABLE_SCHEMA = DATABASE()
        ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
        """;

    public async Task TestAsync(LocalConnection connection, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(WorkspaceLimits.ConnectTimeoutSeconds));
        await using var database = LocalConnectionFactory.Create(connection);
        await database.OpenAsync(deadline.Token);
        if (connection.Provider == "sqlserver")
            await ReadOnlyPrincipalGuard.VerifyAsync(database, deadline.Token);
        logger.LogInformation("Local connection test completed for {ConnectionId} using {Provider}", connection.Id, connection.Provider);
    }

    public async Task<LocalQueryResult> DiscoverAsync(LocalConnection connection, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(WorkspaceLimits.ConnectTimeoutSeconds));
        await using var database = LocalConnectionFactory.Create(connection);
        await database.OpenAsync(deadline.Token);
        if (connection.Provider == "sqlserver")
            await ReadOnlyPrincipalGuard.VerifyAsync(database, deadline.Token);
        await using var command = database.CreateCommand();
        // Only this fixed metadata query runs on providers without a dialect-specific guard.
        command.CommandText = connection.Provider switch
        {
            "postgres" => DiscoverySql.Replace("INFORMATION_SCHEMA", "information_schema", StringComparison.Ordinal),
            "mysql" => MySqlDiscoverySql,
            _ => DiscoverySql
        };
        if (connection.Provider == "sqlserver")
            SqlServerDiscovery.Configure(command, connection.AllowedTables);
        command.CommandTimeout = WorkspaceLimits.ConnectTimeoutSeconds;
        return await LocalResultReader.ReadAsync(command, WorkspaceLimits.MaxRows, deadline.Token);
    }

    public async Task<LocalQueryResult> QueryAsync(
        LocalConnection connection, LocalQueryRequest request, CancellationToken cancellationToken)
    {
        WorkspaceValidation.Query(request);
        if (connection.Id != request.ConnectionId)
            throw new ArgumentException("Bağlantı kimliği uyuşmuyor.");
        if (connection.Provider != "sqlserver")
            throw new NotSupportedException("PostgreSQL/MySQL: bağlantı testi ve şema keşfi kullanılabilir. Serbest SQL, sağlayıcıya özel salt-okunur yetki doğrulaması eklenene kadar kapalıdır.");
        // QueryExecutor is coupled to enrolled BridgeState; reuse its independent safeguards, not its authority.
        if (!ReadOnlySql.IsReadOnly(request.Sql))
            throw new ArgumentException("Yalnızca salt-okunur SELECT sorguları desteklenir.");
        var validation = SqlPolicy.Validate(request.Sql, connection.AllowedTables, allowMetadata: false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        await using var database = LocalConnectionFactory.Create(connection);
        await database.OpenAsync(deadline.Token);
        await ReadOnlyPrincipalGuard.VerifyAsync(database, deadline.Token);
        await ReadOnlyPrincipalGuard.VerifyTablesAsync(database, validation, deadline.Token);
        await using var command = database.CreateCommand();
        command.CommandText = request.Sql;
        command.CommandTimeout = request.TimeoutSeconds;
        var result = await LocalResultReader.ReadAsync(command, request.MaxRows, deadline.Token);
        logger.LogInformation("Local query completed for {ConnectionId}: {RowCount} rows, truncated {Truncated}",
            connection.Id, result.Rows.Count, result.Truncated);
        return result;
    }
}
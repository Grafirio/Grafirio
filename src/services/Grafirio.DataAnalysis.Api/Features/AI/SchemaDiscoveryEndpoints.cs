using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Features.AI;

public static class SchemaDiscoveryEndpoints
{
    public static void MapSchemaDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/schema-discovery");

        group.MapPost("/analyze", AnalyzeSchema)
            .WithName("AnalyzeSchemaWithAI")
            .WithTags("Schema Discovery");
    }

    private static async Task<IResult> AnalyzeSchema(
        SchemaDiscoveryRequest request,
        ILogger<SchemaDiscoveryRequest> logger)
    {
        try
        {
            logger.LogInformation("Starting schema discovery for database: {Database}", request.ConnectionInfo.Database);

            var connectionString = BuildConnectionString(request.ConnectionInfo);
            var sampleData = new Dictionary<string, List<Dictionary<string, object>>>();

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // Her tablo için 100 satır örnek veri çek
            foreach (var tableName in request.Tables)
            {
                logger.LogInformation("Fetching sample data from table: {TableName}", tableName);

                var query = $@"
                    SELECT TOP 100 * 
                    FROM {tableName}
                    ORDER BY (SELECT NULL)"; // Random order için

                var rows = await connection.QueryAsync(query);
                var tableData = new List<Dictionary<string, object>>();

                foreach (var row in rows)
                {
                    var rowDict = new Dictionary<string, object>();
                    var rowData = (IDictionary<string, object>)row;
                    
                    foreach (var kvp in rowData)
                    {
                        rowDict[kvp.Key] = kvp.Value ?? DBNull.Value;
                    }
                    
                    tableData.Add(rowDict);
                }

                sampleData[tableName] = tableData;
            }

            // Tablo şema bilgilerini çek
            var schemaInfo = new List<TableSchemaInfo>();
            
            foreach (var tableName in request.Tables)
            {
                var schemaQuery = @"
                    SELECT 
                        c.COLUMN_NAME as ColumnName,
                        c.DATA_TYPE as DataType,
                        c.IS_NULLABLE as IsNullable,
                        c.CHARACTER_MAXIMUM_LENGTH as MaxLength,
                        CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END as IsPrimaryKey,
                        CASE WHEN fk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END as IsForeignKey
                    FROM INFORMATION_SCHEMA.COLUMNS c
                    LEFT JOIN (
                        SELECT ku.COLUMN_NAME
                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                            ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                        AND ku.TABLE_NAME = @TableName
                    ) pk ON c.COLUMN_NAME = pk.COLUMN_NAME
                    LEFT JOIN (
                        SELECT ku.COLUMN_NAME
                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                            ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                        WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
                        AND ku.TABLE_NAME = @TableName
                    ) fk ON c.COLUMN_NAME = fk.COLUMN_NAME
                    WHERE c.TABLE_NAME = @TableName
                    ORDER BY c.ORDINAL_POSITION";

                var columns = await connection.QueryAsync<ColumnSchemaInfo>(
                    schemaQuery,
                    new { TableName = tableName.Replace("dbo.", "") }
                );

                schemaInfo.Add(new TableSchemaInfo
                {
                    TableName = tableName,
                    Columns = columns.ToList(),
                    SampleRowCount = sampleData[tableName].Count
                });
            }

            var response = new SchemaDiscoveryResponse
            {
                Success = true,
                Database = request.ConnectionInfo.Database,
                TablesAnalyzed = request.Tables.Count,
                SchemaInfo = schemaInfo,
                SampleData = sampleData,
                Message = "Schema discovery completed successfully. Ready to send to AI for analysis."
            };

            logger.LogInformation(
                "Schema discovery completed. Tables: {Count}, Total sample rows: {Rows}",
                request.Tables.Count,
                sampleData.Values.Sum(x => x.Count)
            );

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during schema discovery");
            return Results.BadRequest(new
            {
                success = false,
                message = $"Schema discovery failed: {ex.Message}"
            });
        }
    }

    private static string BuildConnectionString(SchemaConnectionInfo connectionInfo)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = connectionInfo.Port == 1433 
                ? connectionInfo.Host 
                : $"{connectionInfo.Host},{connectionInfo.Port}",
            InitialCatalog = connectionInfo.Database,
            UserID = connectionInfo.Username,
            Password = connectionInfo.Password,
            TrustServerCertificate = connectionInfo.TrustServerCertificate,
            IntegratedSecurity = false,
            ConnectTimeout = 30
        };

        return builder.ConnectionString;
    }
}

public record SchemaDiscoveryRequest(
    SchemaConnectionInfo ConnectionInfo,
    List<string> Tables
);

public record SchemaConnectionInfo(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate
);

public record SchemaDiscoveryResponse
{
    public bool Success { get; init; }
    public string Database { get; init; } = string.Empty;
    public int TablesAnalyzed { get; init; }
    public List<TableSchemaInfo> SchemaInfo { get; init; } = new();
    public Dictionary<string, List<Dictionary<string, object>>> SampleData { get; init; } = new();
    public string Message { get; init; } = string.Empty;
}

public record TableSchemaInfo
{
    public string TableName { get; init; } = string.Empty;
    public List<ColumnSchemaInfo> Columns { get; init; } = new();
    public int SampleRowCount { get; init; }
}

public record ColumnSchemaInfo
{
    public string ColumnName { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string IsNullable { get; init; } = string.Empty;
    public int? MaxLength { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsForeignKey { get; init; }
}

using Dapper;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.DataAnalysis.Api.Models;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Features.Schema;

public static class SchemaEndpoints
{
    public static void MapSchemaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/schema")
            .WithTags("Schema Discovery")
            .WithOpenApi();

        group.MapPost("/tables", GetTables)
            .WithName("GetTables")
            .WithDescription("List all tables in the connected database");

        group.MapPost("/table/{tableName}", GetTableSchema)
            .WithName("GetTableSchema")
            .WithDescription("Get detailed schema information for a specific table");
    }

    private static async Task<IResult> GetTables(SqlConnectionRequest request)
    {
        try
        {
            var connectionString = ConnectionTestEndpoints.BuildConnectionString(request);
            
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    t.TABLE_NAME as TableName,
                    t.TABLE_SCHEMA as Schema,
                    (SELECT COUNT(*) FROM [' + t.TABLE_SCHEMA + '].[' + t.TABLE_NAME + ']) as RowCount
                FROM INFORMATION_SCHEMA.TABLES t
                WHERE t.TABLE_TYPE = 'BASE TABLE'
                ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME";

            // Basit versiyon - satır sayısı olmadan
            var simpleQuery = @"
                SELECT 
                    TABLE_NAME as TableName,
                    TABLE_SCHEMA as [Schema]
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_SCHEMA, TABLE_NAME";

            var tables = await connection.QueryAsync<dynamic>(simpleQuery);
            
            var tableList = tables.Select(t => new 
            {
                tableName = (string)t.TableName,
                schema = (string)t.Schema,
                fullName = $"{t.Schema}.{t.TableName}"
            }).ToList();

            return Results.Ok(new { 
                success = true,
                count = tableList.Count,
                tables = tableList 
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { 
                success = false, 
                message = $"Failed to retrieve tables: {ex.Message}" 
            });
        }
    }

    private static async Task<IResult> GetTableSchema(
        string tableName, 
        SqlConnectionRequest request)
    {
        try
        {
            var connectionString = ConnectionTestEndpoints.BuildConnectionString(request);
            
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // Tablo ve şema adını ayır
            var parts = tableName.Split('.');
            var schema = parts.Length > 1 ? parts[0] : "dbo";
            var table = parts.Length > 1 ? parts[1] : tableName;

            var query = @"
                SELECT 
                    COLUMN_NAME as ColumnName,
                    DATA_TYPE as DataType,
                    CAST(CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS BIT) as IsNullable,
                    CHARACTER_MAXIMUM_LENGTH as MaxLength
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table
                ORDER BY ORDINAL_POSITION";

            var columns = await connection.QueryAsync<ColumnInfo>(
                query, 
                new { Schema = schema, Table = table }
            );

            // Satır sayısını al
            var countQuery = $"SELECT COUNT(*) FROM [{schema}].[{table}]";
            var rowCount = await connection.ExecuteScalarAsync<int>(countQuery);

            return Results.Ok(new TableSchemaResponse(
                TableName: table,
                Schema: schema,
                Columns: columns.ToList(),
                RowCount: rowCount
            ));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                Success = false, 
                Message = $"Failed to retrieve table schema: {ex.Message}" 
            });
        }
    }
}

using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Models;

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

    private static async Task<IResult> GetTables(
        SqlConnectionRequest request,
        IDataSourceFactory dataSources,
        CancellationToken ct)
    {
        try
        {
            await using var session = await dataSources.OpenAsync(DataSourceTarget.From(request), ct);

            // Satir sayisi bilerek cekilmiyor: tablo listesi ekrani icin her
            // tabloya COUNT(*) atmak buyuk veritabanlarinda dakikalar suruyor.
            var rows = await session.QueryRowsAsync(@"
                SELECT
                    TABLE_NAME   AS TableName,
                    TABLE_SCHEMA AS [Schema]
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_SCHEMA, TABLE_NAME", ct: ct);

            var tableList = rows.Select(row =>
            {
                var schema = row.GetRequiredString("Schema");
                var tableName = row.GetRequiredString("TableName");
                return new
                {
                    tableName,
                    schema,
                    fullName = $"{schema}.{tableName}"
                };
            }).ToList();

            return Results.Ok(new
            {
                success = true,
                count = tableList.Count,
                tables = tableList
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                success = false,
                message = $"Failed to retrieve tables: {ex.Message}"
            });
        }
    }

    private static async Task<IResult> GetTableSchema(
        string tableName,
        SqlConnectionRequest request,
        IDataSourceFactory dataSources,
        CancellationToken ct)
    {
        try
        {
            await using var session = await dataSources.OpenAsync(DataSourceTarget.From(request), ct);

            // Tablo ve şema adını ayır
            var parts = tableName.Split('.');
            var schema = parts.Length > 1 ? parts[0] : "dbo";
            var table = parts.Length > 1 ? parts[1] : tableName;

            var columns = await session.QueryAsync<ColumnInfo>(@"
                SELECT
                    COLUMN_NAME as ColumnName,
                    DATA_TYPE as DataType,
                    CAST(CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS BIT) as IsNullable,
                    CHARACTER_MAXIMUM_LENGTH as MaxLength
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table
                ORDER BY ORDINAL_POSITION",
                new { Schema = schema, Table = table }, ct: ct);

            var rowCount = await session.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM {Quote(schema)}.{Quote(table)}", ct: ct);

            return Results.Ok(new TableSchemaResponse(
                TableName: table,
                Schema: schema,
                Columns: columns.ToList(),
                RowCount: rowCount
            ));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = $"Failed to retrieve table schema: {ex.Message}"
            });
        }
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}

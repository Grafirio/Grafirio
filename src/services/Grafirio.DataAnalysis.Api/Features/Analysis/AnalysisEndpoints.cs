using Dapper;
using Grafirio.DataAnalysis.Api.Features.Connection;
using Grafirio.DataAnalysis.Api.Models;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Features.Analysis;

public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analysis")
            .WithTags("Data Analysis")
            .WithOpenApi();

        group.MapPost("/data-quality", GetDataQuality)
            .WithName("GetDataQuality")
            .WithDescription("Analyze data quality (NULL values, duplicates)");

        group.MapPost("/statistics", GetStatistics)
            .WithName("GetStatistics")
            .WithDescription("Get statistical summary of tables");

        group.MapPost("/missing-data", GetMissingData)
            .WithName("GetMissingData")
            .WithDescription("Detailed missing data analysis");

        group.MapPost("/relationships", GetRelationships)
            .WithName("GetRelationships")
            .WithDescription("Detect foreign key relationships");
    }

    private static async Task<IResult> GetDataQuality(AnalysisRequest request)
    {
        try
        {
            var connectionString = ConnectionEndpoints.BuildConnectionString(request.ConnectionInfo);
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var results = new List<TableQualityInfo>();

            foreach (var table in request.Tables)
            {
                var parts = table.Split('.');
                var schema = parts.Length > 1 ? parts[0] : "dbo";
                var tableName = parts.Length > 1 ? parts[1] : table;

                // Toplam satır sayısı
                var totalRowsQuery = $"SELECT COUNT(*) FROM [{schema}].[{tableName}]";
                var totalRows = await connection.ExecuteScalarAsync<int>(totalRowsQuery);

                // Her kolonun NULL sayısı
                var columnsQuery = @"
                    SELECT COLUMN_NAME
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @TableName";
                
                var columns = await connection.QueryAsync<string>(columnsQuery, new { Schema = schema, TableName = tableName });

                var nullCounts = new Dictionary<string, int>();
                foreach (var column in columns)
                {
                    var nullCountQuery = $"SELECT COUNT(*) FROM [{schema}].[{tableName}] WHERE [{column}] IS NULL";
                    var nullCount = await connection.ExecuteScalarAsync<int>(nullCountQuery);
                    nullCounts[column] = nullCount;
                }

                // Duplicate satır sayısı (tüm kolonlara göre)
                var duplicateQuery = $@"
                    SELECT COUNT(*) - COUNT(DISTINCT *)
                    FROM (SELECT * FROM [{schema}].[{tableName}]) AS t";
                
                // Basit duplicate kontrolü - ilk kolona göre
                var firstColumn = columns.FirstOrDefault();
                var duplicateCount = 0;
                if (firstColumn != null)
                {
                    var dupQuery = $@"
                        SELECT COUNT(*) 
                        FROM [{schema}].[{tableName}]
                        GROUP BY [{firstColumn}]
                        HAVING COUNT(*) > 1";
                    var duplicates = await connection.QueryAsync<int>(dupQuery);
                    duplicateCount = duplicates.Count();
                }

                results.Add(new TableQualityInfo
                {
                    TableName = tableName,
                    Schema = schema,
                    TotalRows = totalRows,
                    NullCounts = nullCounts,
                    TotalNulls = nullCounts.Values.Sum(),
                    DuplicateRows = duplicateCount,
                    QualityScore = CalculateQualityScore(totalRows, nullCounts.Values.Sum(), duplicateCount)
                });
            }

            return Results.Ok(new { 
                success = true, 
                data = results,
                summary = new {
                    totalTables = results.Count,
                    averageQuality = results.Average(r => r.QualityScore)
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { 
                success = false, 
                message = $"Data quality analysis failed: {ex.Message}" 
            });
        }
    }

    private static async Task<IResult> GetStatistics(AnalysisRequest request)
    {
        try
        {
            var connectionString = ConnectionEndpoints.BuildConnectionString(request.ConnectionInfo);
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var results = new List<TableStatistics>();

            foreach (var table in request.Tables)
            {
                var parts = table.Split('.');
                var schema = parts.Length > 1 ? parts[0] : "dbo";
                var tableName = parts.Length > 1 ? parts[1] : table;

                // Satır sayısı
                var rowCountQuery = $"SELECT COUNT(*) FROM [{schema}].[{tableName}]";
                var rowCount = await connection.ExecuteScalarAsync<int>(rowCountQuery);

                // Kolon sayısı
                var columnCountQuery = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @TableName";
                var columnCount = await connection.ExecuteScalarAsync<int>(columnCountQuery, new { Schema = schema, TableName = tableName });

                // Numeric kolonların istatistikleri
                var numericStatsQuery = @"
                    SELECT 
                        c.COLUMN_NAME,
                        c.DATA_TYPE
                    FROM INFORMATION_SCHEMA.COLUMNS c
                    WHERE c.TABLE_SCHEMA = @Schema 
                    AND c.TABLE_NAME = @TableName
                    AND c.DATA_TYPE IN ('int', 'bigint', 'decimal', 'numeric', 'float', 'real', 'money')";
                
                var numericColumns = await connection.QueryAsync<dynamic>(numericStatsQuery, new { Schema = schema, TableName = tableName });

                var columnStats = new List<ColumnStatistics>();
                foreach (var col in numericColumns)
                {
                    var colName = (string)col.COLUMN_NAME;
                    var statsQuery = $@"
                        SELECT 
                            MIN([{colName}]) as MinValue,
                            MAX([{colName}]) as MaxValue,
                            AVG(CAST([{colName}] AS FLOAT)) as AvgValue,
                            COUNT(DISTINCT [{colName}]) as DistinctCount
                        FROM [{schema}].[{tableName}]
                        WHERE [{colName}] IS NOT NULL";
                    
                    var stats = await connection.QueryFirstOrDefaultAsync<dynamic>(statsQuery);
                    if (stats != null)
                    {
                        columnStats.Add(new ColumnStatistics
                        {
                            ColumnName = colName,
                            DataType = (string)col.DATA_TYPE,
                            MinValue = stats.MinValue?.ToString(),
                            MaxValue = stats.MaxValue?.ToString(),
                            AvgValue = stats.AvgValue?.ToString(),
                            DistinctCount = stats.DistinctCount
                        });
                    }
                }

                results.Add(new TableStatistics
                {
                    TableName = tableName,
                    Schema = schema,
                    RowCount = rowCount,
                    ColumnCount = columnCount,
                    NumericColumns = columnStats
                });
            }

            return Results.Ok(new { 
                success = true, 
                data = results
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { 
                success = false, 
                message = $"Statistics analysis failed: {ex.Message}" 
            });
        }
    }

    private static async Task<IResult> GetMissingData(AnalysisRequest request)
    {
        try
        {
            var connectionString = ConnectionEndpoints.BuildConnectionString(request.ConnectionInfo);
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var results = new List<MissingDataInfo>();

            foreach (var table in request.Tables)
            {
                var parts = table.Split('.');
                var schema = parts.Length > 1 ? parts[0] : "dbo";
                var tableName = parts.Length > 1 ? parts[1] : table;

                var totalRowsQuery = $"SELECT COUNT(*) FROM [{schema}].[{tableName}]";
                var totalRows = await connection.ExecuteScalarAsync<int>(totalRowsQuery);

                var columnsQuery = @"
                    SELECT 
                        COLUMN_NAME,
                        DATA_TYPE,
                        IS_NULLABLE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @TableName";
                
                var columns = await connection.QueryAsync<dynamic>(columnsQuery, new { Schema = schema, TableName = tableName });

                var missingInfo = new List<ColumnMissingInfo>();
                foreach (var col in columns)
                {
                    var colName = (string)col.COLUMN_NAME;
                    var nullCountQuery = $"SELECT COUNT(*) FROM [{schema}].[{tableName}] WHERE [{colName}] IS NULL";
                    var nullCount = await connection.ExecuteScalarAsync<int>(nullCountQuery);
                    
                    var missingPercentage = totalRows > 0 ? (nullCount * 100.0 / totalRows) : 0;

                    missingInfo.Add(new ColumnMissingInfo
                    {
                        ColumnName = colName,
                        DataType = (string)col.DATA_TYPE,
                        IsNullable = (string)col.IS_NULLABLE == "YES",
                        MissingCount = nullCount,
                        MissingPercentage = Math.Round(missingPercentage, 2)
                    });
                }

                results.Add(new MissingDataInfo
                {
                    TableName = tableName,
                    Schema = schema,
                    TotalRows = totalRows,
                    Columns = missingInfo,
                    TotalMissingCells = missingInfo.Sum(c => c.MissingCount),
                    AverageMissingPercentage = missingInfo.Average(c => c.MissingPercentage)
                });
            }

            return Results.Ok(new { 
                success = true, 
                data = results
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { 
                success = false, 
                message = $"Missing data analysis failed: {ex.Message}" 
            });
        }
    }

    private static async Task<IResult> GetRelationships(AnalysisRequest request)
    {
        try
        {
            var connectionString = ConnectionEndpoints.BuildConnectionString(request.ConnectionInfo);
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var relationshipsQuery = @"
                SELECT 
                    fk.name AS ConstraintName,
                    tp.name AS ParentTable,
                    cp.name AS ParentColumn,
                    tr.name AS ReferencedTable,
                    cr.name AS ReferencedColumn,
                    OBJECT_SCHEMA_NAME(fk.parent_object_id) AS ParentSchema,
                    OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ReferencedSchema
                FROM sys.foreign_keys AS fk
                INNER JOIN sys.foreign_key_columns AS fkc 
                    ON fk.object_id = fkc.constraint_object_id
                INNER JOIN sys.tables AS tp 
                    ON fkc.parent_object_id = tp.object_id
                INNER JOIN sys.columns AS cp 
                    ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
                INNER JOIN sys.tables AS tr 
                    ON fkc.referenced_object_id = tr.object_id
                INNER JOIN sys.columns AS cr 
                    ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id";

            var allRelationships = await connection.QueryAsync<dynamic>(relationshipsQuery);

            // Sadece seçili tabloları filtrele
            var relevantRelationships = allRelationships
                .Where(r => request.Tables.Any(t => t.Contains((string)r.ParentTable) || t.Contains((string)r.ReferencedTable)))
                .Select(r => new RelationshipInfo
                {
                    ConstraintName = (string)r.ConstraintName,
                    ParentTable = $"{r.ParentSchema}.{r.ParentTable}",
                    ParentColumn = (string)r.ParentColumn,
                    ReferencedTable = $"{r.ReferencedSchema}.{r.ReferencedTable}",
                    ReferencedColumn = (string)r.ReferencedColumn
                })
                .ToList();

            return Results.Ok(new { 
                success = true, 
                data = relevantRelationships,
                count = relevantRelationships.Count
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { 
                success = false, 
                message = $"Relationship analysis failed: {ex.Message}" 
            });
        }
    }

    private static double CalculateQualityScore(int totalRows, int totalNulls, int duplicates)
    {
        if (totalRows == 0) return 0;
        
        var nullPenalty = (totalNulls * 100.0 / (totalRows * 10)); // Assume 10 columns average
        var duplicatePenalty = (duplicates * 100.0 / totalRows);
        
        var score = 100 - nullPenalty - duplicatePenalty;
        return Math.Max(0, Math.Min(100, score));
    }
}

// Response Models
public class TableQualityInfo
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public Dictionary<string, int> NullCounts { get; set; } = new();
    public int TotalNulls { get; set; }
    public int DuplicateRows { get; set; }
    public double QualityScore { get; set; }
}

public class TableStatistics
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<ColumnStatistics> NumericColumns { get; set; } = new();
}

public class ColumnStatistics
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public string? AvgValue { get; set; }
    public int DistinctCount { get; set; }
}

public class MissingDataInfo
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public List<ColumnMissingInfo> Columns { get; set; } = new();
    public int TotalMissingCells { get; set; }
    public double AverageMissingPercentage { get; set; }
}

public class ColumnMissingInfo
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public int MissingCount { get; set; }
    public double MissingPercentage { get; set; }
}

public class RelationshipInfo
{
    public string ConstraintName { get; set; } = string.Empty;
    public string ParentTable { get; set; } = string.Empty;
    public string ParentColumn { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public string ReferencedColumn { get; set; } = string.Empty;
}

public record AnalysisRequest(
    SqlConnectionRequest ConnectionInfo,
    List<string> Tables
);

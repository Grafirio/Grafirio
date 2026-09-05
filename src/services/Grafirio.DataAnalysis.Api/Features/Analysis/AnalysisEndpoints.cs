using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.QueryPolicy;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grafirio.DataAnalysis.Api.Features.Analysis;

/// <summary>
/// On analiz uclari: veri kalitesi, istatistik, eksik veri, iliskiler.
///
/// Uclar kayitli baglanti kimligi aliyor, ham kimlik bilgisi degil — sebebi
/// <see cref="Grafirio.DataAnalysis.Api.Features.Schema.SchemaEndpoints"/>
/// ile ayni: <c>DataSourceTarget</c> alan overload bridge'i atlayip her zaman
/// buluttan dogrudan TCP aciyor, ve arayuzun bu uclari cagirabilmek icin
/// veritabani parolasini tarayiciya indirmesi gerekiyordu.
/// </summary>
public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        // Politika adli: paylasilan kurulumda varsayilan sema yok, ciplak
        // RequireAuthorization() 400 doner.
        var group = app.MapGroup("/api/analysis/{connectionId:guid}")
            .RequireAuthorization("CompanyAccess")
            .RequirePermission(AppPermissions.DataSourcesRead)
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

    private static async Task<IResult> GetDataQuality(
        Guid connectionId,
        AnalysisRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        [FromServices] IDataSourceFactory dataSources,
        [FromServices] ConnectionProfileStore profiles,
        CancellationToken ct)
    {
        var (connection, error) = await ConnectionScope.ResolveAsync(db, identity, connectionId, ct);
        if (error is not null) return error;

        try
        {
            var tables = QueryTableScope.RequireSelected(request.Tables,
                await profiles.GetSelectedTablesAsync(connectionId, connection!.CompanyId, ct));
            await using var session = await dataSources.OpenAsync(connection!, ct);

            var results = new List<TableQualityInfo>();

            foreach (var table in tables)
            {
                var (schema, tableName) = SplitTableName(table);
                var qualified = $"{Quote(schema)}.{Quote(tableName)}";

                // Toplam satır sayısı
                var totalRows = await session.ScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {qualified}", ct: ct);

                // Her kolonun NULL sayısı
                var columns = await session.QueryAsync<string>(@"
                    SELECT COLUMN_NAME
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @TableName",
                    new { Schema = schema, TableName = tableName }, ct: ct);

                var nullCounts = new Dictionary<string, int>();
                foreach (var column in columns)
                {
                    nullCounts[column] = await session.ScalarAsync<int>(
                        $"SELECT COUNT(*) FROM {qualified} WHERE {Quote(column)} IS NULL", ct: ct);
                }

                // Basit duplicate kontrolü - ilk kolona göre
                var firstColumn = columns.FirstOrDefault();
                var duplicateCount = 0;
                if (firstColumn != null)
                {
                    var duplicates = await session.QueryAsync<int>($@"
                        SELECT COUNT(*)
                        FROM {qualified}
                        GROUP BY {Quote(firstColumn)}
                        HAVING COUNT(*) > 1", ct: ct);
                    duplicateCount = duplicates.Count;
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
        catch (QueryPolicyException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { 
                success = false, 
                message = $"Data quality analysis failed: {ex.Message}" 
            });
        }
    }

    private static async Task<IResult> GetStatistics(
        Guid connectionId,
        AnalysisRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        [FromServices] IDataSourceFactory dataSources,
        [FromServices] ConnectionProfileStore profiles,
        CancellationToken ct)
    {
        var (connection, error) = await ConnectionScope.ResolveAsync(db, identity, connectionId, ct);
        if (error is not null) return error;

        try
        {
            var tables = QueryTableScope.RequireSelected(request.Tables,
                await profiles.GetSelectedTablesAsync(connectionId, connection!.CompanyId, ct));
            await using var session = await dataSources.OpenAsync(connection!, ct);

            var results = new List<TableStatistics>();

            foreach (var table in tables)
            {
                var (schema, tableName) = SplitTableName(table);
                var qualified = $"{Quote(schema)}.{Quote(tableName)}";

                // Satır sayısı
                var rowCount = await session.ScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {qualified}", ct: ct);

                // Kolon sayısı
                var columnCount = await session.ScalarAsync<int>(@"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @TableName",
                    new { Schema = schema, TableName = tableName }, ct: ct);

                // Numeric kolonların istatistikleri
                var numericColumns = await session.QueryRowsAsync(@"
                    SELECT
                        c.COLUMN_NAME,
                        c.DATA_TYPE
                    FROM INFORMATION_SCHEMA.COLUMNS c
                    WHERE c.TABLE_SCHEMA = @Schema
                    AND c.TABLE_NAME = @TableName
                    AND c.DATA_TYPE IN ('int', 'bigint', 'decimal', 'numeric', 'float', 'real', 'money')",
                    new { Schema = schema, TableName = tableName }, ct: ct);

                var columnStats = new List<ColumnStatistics>();
                foreach (var col in numericColumns)
                {
                    var colName = col.GetRequiredString("COLUMN_NAME");
                    var stats = await session.QueryFirstRowOrDefaultAsync($@"
                        SELECT
                            MIN({Quote(colName)}) as MinValue,
                            MAX({Quote(colName)}) as MaxValue,
                            AVG(CAST({Quote(colName)} AS FLOAT)) as AvgValue,
                            COUNT(DISTINCT {Quote(colName)}) as DistinctCount
                        FROM {qualified}
                        WHERE {Quote(colName)} IS NOT NULL", ct: ct);

                    if (stats != null)
                    {
                        columnStats.Add(new ColumnStatistics
                        {
                            ColumnName = colName,
                            DataType = col.GetString("DATA_TYPE") ?? "",
                            MinValue = stats.GetString("MinValue"),
                            MaxValue = stats.GetString("MaxValue"),
                            AvgValue = stats.GetString("AvgValue"),
                            DistinctCount = stats.GetInt32("DistinctCount")
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
        catch (QueryPolicyException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { 
                success = false, 
                message = $"Statistics analysis failed: {ex.Message}" 
            });
        }
    }

    private static async Task<IResult> GetMissingData(
        Guid connectionId,
        AnalysisRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        [FromServices] IDataSourceFactory dataSources,
        [FromServices] ConnectionProfileStore profiles,
        CancellationToken ct)
    {
        var (connection, error) = await ConnectionScope.ResolveAsync(db, identity, connectionId, ct);
        if (error is not null) return error;

        try
        {
            var tables = QueryTableScope.RequireSelected(request.Tables,
                await profiles.GetSelectedTablesAsync(connectionId, connection!.CompanyId, ct));
            await using var session = await dataSources.OpenAsync(connection!, ct);

            var results = new List<MissingDataInfo>();

            foreach (var table in tables)
            {
                var (schema, tableName) = SplitTableName(table);
                var qualified = $"{Quote(schema)}.{Quote(tableName)}";

                var totalRows = await session.ScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {qualified}", ct: ct);

                var columns = await session.QueryRowsAsync(@"
                    SELECT
                        COLUMN_NAME,
                        DATA_TYPE,
                        IS_NULLABLE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @TableName",
                    new { Schema = schema, TableName = tableName }, ct: ct);

                var missingInfo = new List<ColumnMissingInfo>();
                foreach (var col in columns)
                {
                    var colName = col.GetRequiredString("COLUMN_NAME");
                    var nullCount = await session.ScalarAsync<int>(
                        $"SELECT COUNT(*) FROM {qualified} WHERE {Quote(colName)} IS NULL", ct: ct);

                    var missingPercentage = totalRows > 0 ? (nullCount * 100.0 / totalRows) : 0;

                    missingInfo.Add(new ColumnMissingInfo
                    {
                        ColumnName = colName,
                        DataType = col.GetString("DATA_TYPE") ?? "",
                        IsNullable = col.GetString("IS_NULLABLE") == "YES",
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
        catch (QueryPolicyException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { 
                success = false, 
                message = $"Missing data analysis failed: {ex.Message}" 
            });
        }
    }

    private static async Task<IResult> GetRelationships(
        Guid connectionId,
        AnalysisRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        [FromServices] IDataSourceFactory dataSources,
        [FromServices] ConnectionProfileStore profiles,
        CancellationToken ct)
    {
        var (connection, error) = await ConnectionScope.ResolveAsync(db, identity, connectionId, ct);
        if (error is not null) return error;

        try
        {
            var tables = QueryTableScope.RequireSelected(request.Tables,
                await profiles.GetSelectedTablesAsync(connectionId, connection!.CompanyId, ct));
            var requestedIdentities = tables.Select(Grafirio.QueryPolicy.QueryPolicy.ParseTableIdentity).ToHashSet();
            await using var session = await dataSources.OpenAsync(connection!, ct);

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
                        ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
                    WHERE OBJECT_SCHEMA_NAME(fk.parent_object_id) + '.' + tp.name IN @Names
                      AND OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + tr.name IN @Names";

                    var allRelationships = await session.QueryRowsAsync(relationshipsQuery, new { Names = tables }, ct: ct);

            // Both ends must be in scope; substring matching can expose unrelated tables.
            var relevantRelationships = allRelationships
                .Where(r => requestedIdentities.Contains(new SqlTableIdentity(
                        r.GetRequiredString("ParentSchema"), r.GetRequiredString("ParentTable")))
                    && requestedIdentities.Contains(new SqlTableIdentity(
                        r.GetRequiredString("ReferencedSchema"), r.GetRequiredString("ReferencedTable"))))
                .Select(r => new RelationshipInfo
                {
                    ConstraintName = r.GetRequiredString("ConstraintName"),
                    ParentTable = $"{r.GetString("ParentSchema")}.{r.GetString("ParentTable")}",
                    ParentColumn = r.GetRequiredString("ParentColumn"),
                    ReferencedTable = $"{r.GetString("ReferencedSchema")}.{r.GetString("ReferencedTable")}",
                    ReferencedColumn = r.GetRequiredString("ReferencedColumn")
                })
                .ToList();

            return Results.Ok(new { 
                success = true, 
                data = relevantRelationships,
                count = relevantRelationships.Count
            });
        }
        catch (QueryPolicyException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { 
                success = false, 
                message = $"Relationship analysis failed: {ex.Message}" 
            });
        }
    }

    private static (string Schema, string Table) SplitTableName(string qualified)
    {
        var identity = Grafirio.QueryPolicy.QueryPolicy.ParseTableIdentity(qualified);
        return (identity.Schema!, identity.Name);
    }

    /// <summary>
    /// Tablo ve kolon adlari sorguya parametre olarak degil metin olarak
    /// giriyor (SQL'de tanimlayici parametrelenemez). En azindan koseli parantez
    /// kacisi yapiliyor; onceden o da yoktu.
    /// </summary>
    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

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

/// <summary>
/// Hangi tablolar analiz edilecek. Baglantinin kendisi artik govdede degil
/// yolda: kimlik bilgisini istekte tasimak, bridge yolunu kullanilamaz
/// kiliyor ve parolayi gereksiz yere tarayicidan geciriyordu.
/// </summary>
public record AnalysisRequest(
    List<string> Tables
);

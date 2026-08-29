using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.DataAnalysis.Api.Models;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grafirio.DataAnalysis.Api.Features.Schema;

/// <summary>
/// Tablo ve kolon listesi — tablo secimi ekranini besleyen uclar.
///
/// Uclar kayitli baglanti kimligi aliyor, ham kimlik bilgisi degil. Iki sey
/// birden duzeliyor:
///
///   * Bridge yolu. <c>DataSourceTarget</c> alan overload her zaman buluttan
///     dogrudan TCP aciyor; yani bridge'e bagli bir baglantida tablo listesi
///     hic gelmiyordu. Tablo secilemeyince "Analiz Et" de "Önce analiz
///     edilecek tabloları seçin" ile duruyor ve sorgu hicbir zaman
///     calistirilamiyordu.
///   * Sifre. Onceden arayuz bu uclari cagirabilmek icin <c>/decrypt</c> ile
///     veritabani parolasini tarayiciya indiriyordu. Kimlik yeterli olunca
///     parolanin bulutun disina cikmasi gereken bir sebep kalmiyor.
///
/// Uclar ayrica kimlik dogrulamasi ISTIYOR. Onceden acikti: gecerli bir
/// hesabi olan herkes istekte yazdigi adrese sunucu adina baglanti
/// kurdurabiliyordu.
/// </summary>
public static class SchemaEndpoints
{
    public static void MapSchemaEndpoints(this IEndpointRouteBuilder app)
    {
        // Politika adli: paylasilan kurulumda varsayilan sema yok, ciplak
        // RequireAuthorization() 400 doner.
        var group = app.MapGroup("/api/schema/{connectionId:guid}")
            .RequireAuthorization("CompanyAccess")
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithTags("Schema Discovery")
            .WithOpenApi();

        group.MapGet("/tables", GetTables)
            .WithName("GetTables")
            .WithDescription("Bağlantıdaki tabloları listeler");

        group.MapGet("/table/{tableName}", GetTableSchema)
            .WithName("GetTableSchema")
            .WithDescription("Bir tablonun kolonlarını ve satır sayısını getirir");
    }

    private static async Task<IResult> GetTables(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        [FromServices] IDataSourceFactory dataSources,
        CancellationToken ct)
    {
        var (connection, error) = await ConnectionScope.ResolveAsync(db, identity, connectionId, ct);
        if (error is not null) return error;

        try
        {
            await using var session = await dataSources.OpenAsync(connection!, ct);

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
                message = $"Tablolar getirilemedi: {ex.Message}"
            });
        }
    }

    private static async Task<IResult> GetTableSchema(
        Guid connectionId,
        string tableName,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] IIdentityService identity,
        [FromServices] IDataSourceFactory dataSources,
        CancellationToken ct)
    {
        var (connection, error) = await ConnectionScope.ResolveAsync(db, identity, connectionId, ct);
        if (error is not null) return error;

        try
        {
            await using var session = await dataSources.OpenAsync(connection!, ct);

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
                Message = $"Tablo şeması getirilemedi: {ex.Message}"
            });
        }
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}

using System.Text;
using System.Text.Json;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.QueryPolicy;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Internal;

/// <summary>
/// PyCaret servisinin veri okudugu ic uc.
///
/// Onceden analiz istegiyle birlikte musteri veritabaninin host/kullanici/<b>sifre</b>
/// bilgisi PyCaret'e HTTP govdesinde gonderiliyordu. Iki sorunu vardi:
///
///   1. Sifre gereksiz yere ikinci bir servise, oradan da SQLAlchemy'nin hata
///      metinlerine ve loglara yayiliyordu.
///   2. Musteri veritabani firewall arkasinda oldugunda calisamaz: bridge
///      uzerinden giden yolda sifre bulutta hic bulunmayacak, dolayisiyla
///      gonderilecek bir sey de olmayacak.
///
/// Artik PyCaret urettigi SQL'i buraya gonderiyor, satirlari geri aliyor.
/// Veritabanina nasil ulasildigi (dogrudan mi, bridge uzerinden mi) yalnizca
/// burayi ilgilendiriyor.
///
/// <b>Bu uc gateway'e tanimlanmaz.</b> Yalnizca ic ag icinden, paylasilan
/// anahtarla cagrilir.
/// </summary>
public static class InternalDataEndpoints
{
    public const string ApiKeyHeader = "X-Grafirio-Internal-Key";

    /// <summary>
    /// Tek istekte donebilecek en fazla satir. PyCaret'in kendi tavani
    /// (<c>MAX_ROWS = 50000</c>) ile ayni buyukluk sirasinda; buradaki, o tavani
    /// asan bir istegin sunucuyu yormasini engelleyen ikinci kemer.
    /// </summary>
    private const int MaxRowLimit = 100_000;

    private const int DefaultRowLimit = 50_000;

    /// <summary>Sorgu zaman asimi (saniye).</summary>
    private const int QueryTimeoutSeconds = 300;

    public static void MapInternalDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/data")
            .WithTags("Internal")
            .ExcludeFromDescription();

        group.MapPost("/query", RunQuery);
    }

    private static async Task<IResult> RunQuery(
        InternalQueryRequest request,
        HttpContext http,
        DataAnalysisDbContext db,
        IDataSourceFactory dataSources,
        ConnectionProfileStore profiles,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("InternalDataEndpoints");

        if (!IsAuthorized(http, configuration))
        {
            logger.LogWarning("Internal query rejected: invalid API key.");
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Sql))
            return Results.BadRequest(new { error = "sql boş olamaz." });

        var query = await db.QueryHistories.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == request.QueryId && q.ConfigId == request.ConfigId, ct);
        var config = await db.AnalysisConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ConfigId && c.CompanyId == request.CompanyId &&
                c.ConnectionId == request.ConnectionId, ct);
        var connection = await db.SavedConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ConnectionId && c.CompanyId == request.CompanyId, ct);

        IReadOnlyList<string> allowedTables;
        try
        {
            var selectedTables = config is null ? [] : await profiles.GetSelectedTablesAsync(
                config.ConnectionId, config.CompanyId, ct);
            allowedTables = InternalQueryScope.Validate(
                request.CompanyId, request.QueryId, request.ConfigId, request.ConnectionId, request.ConfigHash,
                query, config, connection, selectedTables);
        }
        catch (QueryPolicyException)
        {
            logger.LogWarning("Internal query rejected: invalid scope for query {QueryId}.", request.QueryId);
            return Results.Json(new { error = "Query execution scope is invalid or stale." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var rowLimit = Math.Clamp(request.MaxRows ?? DefaultRowLimit, 1, MaxRowLimit);

        try
        {
            // Runtime queries use the canonical dictionary; catalog access is reserved for profiling.
                ReadOnlySqlPolicy.Validate(request.Sql, allowedTables, allowMetadata: false);
            await using var session = await dataSources.OpenAsync(connection!, ct);

            // Tavani bir fazlasiyla isteyip kesiyoruz: sonucun kirpilip
            // kirpilmadigini boyle anlayabiliyoruz. Kirpildigini soylememek,
            // kullaniciya tablonun tamamindan cikmis gibi duran eksik bir
            // sonuc gostermek demek.
            var rows = new List<Dictionary<string, object?>>();
            var truncated = false;

            await foreach (var row in session.StreamAsync(
                request.Sql, ToParameters(request.Parameters),
                timeoutSeconds: QueryTimeoutSeconds, maxRows: rowLimit + 1, ct: ct))
            {
                if (rows.Count == rowLimit) { truncated = true; break; }
                rows.Add(new Dictionary<string, object?>(row.Values));
            }

            return Results.Ok(new
            {
                columns = rows.Count > 0 ? rows[0].Keys.ToList() : [],
                rows,
                rowCount = rows.Count,
                truncated
            });
        }
        catch (QueryPolicyException ex)
        {
            logger.LogWarning(ex, "Internal query rejected by SQL policy for query {QueryId}.", request.QueryId);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (DataSourceException ex)
        {
            // Hata metni musteri veritabanindan geliyor; sifre icermez ama
            // yine de disari verilen tek sey mesajin kendisi.
            logger.LogWarning(ex, "Internal query failed for connection {ConnectionId}.", request.ConnectionId);
            return Results.Problem(detail: ex.Message, title: "Sorgu çalıştırılamadı",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static bool IsAuthorized(HttpContext http, IConfiguration configuration)
    {
        var expected = configuration["Internal:ApiKey"]
            ?? Environment.GetEnvironmentVariable("INTERNAL__APIKEY");

        // Anahtar tanimli degilse uc kapalidir. Acik birakmak, ic aga erisen
        // herkese musteri veritabanlarinda sorgu calistirma yetkisi vermek olur.
        if (string.IsNullOrWhiteSpace(expected)) return false;

        if (!http.Request.Headers.TryGetValue(ApiKeyHeader, out var provided)) return false;

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided.ToString()), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>
    /// JSON'dan gelen parametreleri Dapper'in anlayacagi sozluge cevirir.
    /// <see cref="JsonElement"/> oldugu gibi gecerse SQL Server tipini
    /// cozemiyor.
    /// </summary>
    private static Dictionary<string, object?>? ToParameters(
        Dictionary<string, JsonElement>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return null;

        return parameters.ToDictionary(
            kv => kv.Key,
            kv => (object?)(kv.Value.ValueKind switch
            {
                JsonValueKind.String => kv.Value.GetString(),
                JsonValueKind.Number => kv.Value.TryGetInt64(out var l) ? l : kv.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => kv.Value.GetRawText(),
            }));
    }
}

public record InternalQueryRequest(
    Guid ConnectionId,
    string CompanyId,
    Guid QueryId,
    Guid ConfigId,
    string ConfigHash,
    string Sql,
    Dictionary<string, JsonElement>? Parameters = null,
    int? MaxRows = null);

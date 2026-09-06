using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Grafirio.QueryPolicy;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

public static class ConnectionPreviewEndpoints
{
    private const string DefaultDatabase = "master";
    private const string DatabaseListSql = "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name";

    public static void MapConnectionPreviewEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/test", TestAsync)
            .RequirePermission(AppPermissions.DataSourcesCreate)
            .WithName("TestUnsavedConnection");
        group.MapPost("/databases", ListDatabasesAsync)
            .RequirePermission(AppPermissions.DataSourcesCreate)
            .WithName("ListConnectionDatabases");
    }

    private static Task<IResult> TestAsync(ConnectionPreviewRequest request, IIdentityService identity,
        DataAnalysisDbContext db, IDataSourceFactory sources, BridgeStore bridges, CancellationToken ct) =>
        ExecuteAsync(request, identity, db, sources, bridges, listDatabases: false, ct);

    private static Task<IResult> ListDatabasesAsync(ConnectionPreviewRequest request, IIdentityService identity,
        DataAnalysisDbContext db, IDataSourceFactory sources, BridgeStore bridges, CancellationToken ct) =>
        ExecuteAsync(request, identity, db, sources, bridges, listDatabases: true, ct);

    private static async Task<IResult> ExecuteAsync(ConnectionPreviewRequest request, IIdentityService identity,
        DataAnalysisDbContext db, IDataSourceFactory sources, BridgeStore bridges, bool listDatabases, CancellationToken ct)
    {
        if (identity.CurrentCompanyId is not { } companyId) return Results.Forbid();
        var company = companyId.ToString();
        var password = request.Password;
        var route = request.Route;
        try
        {
            if (request.ConnectionId is { } connectionId)
            {
                var saved = await db.SavedConnections.AsNoTracking().FirstOrDefaultAsync(
                    connection => connection.Id == connectionId && connection.CompanyId == company && connection.IsActive, ct);
                if (saved is null) return Results.NotFound(new { error = "Bağlantı bulunamadı" });
                var savedRoute = await bridges.GetConnectionRouteAsync(connectionId, company, ct);
                if (request.ConnectionMode is null && request.BridgeId is null)
                    route = savedRoute;
                if (string.IsNullOrWhiteSpace(password))
                {
                    savedRoute.ValidateBinding(saved);
                    if (!MatchesSavedTarget(request, route, saved, savedRoute))
                        return Results.BadRequest(new { error = "Bağlantı hedefi değişti. Test için veritabanı parolasını yeniden girin." });
                    password = EncryptionHelper.Decrypt(saved.EncryptedPassword);
                }
            }
            if (request.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(password))
                return Results.BadRequest(new { error = "Host, username and password are required." });

            await bridges.ValidateRouteAsync(route, company, ct);
            // A preview is isolated from the saved connection and never changes its credentials or route.
            var target = new DataSourceTarget(request.Host, request.Port,
                string.IsNullOrWhiteSpace(request.Database) ? DefaultDatabase : request.Database,
                request.Username, password, request.TrustServerCertificate)
            {
                CompanyId = company, Route = route
            };
            if (listDatabases)
                ReadOnlySqlPolicy.Validate(DatabaseListSql, allowMetadata: true);
            await using var session = await sources.OpenAsync(target, ct);
            if (listDatabases)
            {
                var rows = await session.QueryRowsAsync(DatabaseListSql,
                    timeoutSeconds: DataSourceTarget.ProbeConnectTimeoutSeconds, ct: ct);
                return Results.Ok(new { success = true, databases = rows.Select(row => row.Values["name"]).ToArray() });
            }
            await session.ScalarAsync<int>("SELECT 1", timeoutSeconds: DataSourceTarget.ProbeConnectTimeoutSeconds, ct: ct);
            return Results.Ok(new { success = true, message = "Bağlantı başarılı" });
        }
        catch (DataSourceException exception)
        {
            return Results.BadRequest(new { success = false, error = exception.Message, message = exception.Message });
        }
        catch (QueryPolicyException)
        {
            return Results.Json(new
            {
                success = false,
                error = "SQL güvenlik politikası bu işlemi engelledi. Veritabanı adını elle girerek bağlantıyı test edin.",
                code = "sql_policy_blocked"
            }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static bool MatchesSavedTarget(ConnectionPreviewRequest request, ConnectionRoute route,
        SavedConnection saved, ConnectionRoute savedRoute) =>
        route.ConnectionMode == savedRoute.ConnectionMode && route.BridgeId == savedRoute.BridgeId &&
        string.Equals(request.Host, saved.Host, StringComparison.OrdinalIgnoreCase) &&
        request.Port == saved.Port && request.Database == saved.Database && request.Username == saved.Username &&
        request.TrustServerCertificate == saved.TrustServerCertificate;
}
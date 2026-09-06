using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Sends credentials only to the explicitly assigned, company-owned bridge.
/// Query sessions resend configuration before execution so reconnect sync is not a readiness guarantee.
/// </summary>
public class BridgeConnectionSync(
    IHubContext<BridgeHub> hub,
    BridgeStore bridges,
    ConnectionProfileStore profiles,
    IServiceScopeFactory scopeFactory,
    ILogger<BridgeConnectionSync> logger)
{
    /// <summary>
    /// Refreshes assigned connections and removes stale copies when a bridge reconnects.
    /// </summary>
    public async Task SyncAllAsync(
        Guid bridgeId, string companyId, string signalRConnectionId, CancellationToken ct = default)
    {
        // Hub'in omru istek disi; DbContext scoped oldugu icin kendi kapsami
        // aciliyor.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();

        var connections = await db.SavedConnections
            .Where(c => c.CompanyId == companyId)
            .ToListAsync(ct);

        if (connections.Count == 0)
        {
            logger.LogInformation(
                "Bridge {BridgeId} için şirkette kayıtlı bağlantı yok; gönderilecek bir şey de yok.",
                bridgeId);
            return;
        }

        foreach (var connection in connections)
        {
            try
            {
                var route = await bridges.GetConnectionRouteAsync(connection.Id, companyId, ct);
                if (!connection.IsActive || !route.Matches(connection) ||
                    route.ConnectionMode != ConnectionRoute.Bridge || route.BridgeId != bridgeId)
                {
                    await hub.Clients.Client(signalRConnectionId).SendAsync(
                        BridgeProtocol.ServerToBridge.RemoveConnection, new RemoveConnectionRequest(connection.Id), ct);
                    continue;
                }
                await bridges.ValidateRouteAsync(route, companyId, ct);
                await SendAsync(signalRConnectionId, bridgeId, connection, ct);
            }
            catch (Exception ex)
            {
                // Bir baglantinin gonderilememesi digerlerini engellememeli;
                // sessiz kalmiyor cunku o baglantiya gelen sorgular
                // "tanimli degil" ile duser.
                logger.LogError(ex,
                    "Bağlantı bridge'e gönderilemedi: {ConnectionId}", connection.Id);
            }
        }

        logger.LogInformation(
            "Bridge {BridgeId} için {Count} bağlantı gönderildi.", bridgeId, connections.Count);
    }

    /// <summary>
    /// Tek bir baglantiyi sirketin cevrimici bridge'ine gonderir. Kayit ve
    /// guncelleme sonrasi cagriliyor.
    /// </summary>
    public async Task SyncOneAsync(
        Guid connectionId, string companyId, BridgeRegistry registry, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();
        var connection = await db.SavedConnections.AsNoTracking().FirstOrDefaultAsync(value =>
            value.Id == connectionId && value.CompanyId == companyId && value.IsActive, ct);
        if (connection is null) return;
        var route = await bridges.GetConnectionRouteAsync(connectionId, companyId, ct);
        route.ValidateBinding(connection);
        if (route.ConnectionMode != ConnectionRoute.Bridge) return;
        await bridges.ValidateRouteAsync(route, companyId, ct);
        var bridgeId = route.BridgeId!.Value;
        if (!registry.IsOnline(bridgeId)) return;

        await SendAsync(
            registry.ConnectionIdOf(bridgeId, companyId), bridgeId, connection, ct);
    }

    /// <summary>Baglantiyi bridge'e unutturur. Silme sonrasi cagriliyor.</summary>
    public virtual async Task ForgetAsync(
        Guid connectionId, string companyId, BridgeRegistry registry, CancellationToken ct = default)
    {
        // Remove stale copies from previous routes as well as the current route.
        foreach (var bridge in await bridges.ListAsync(companyId, ct))
        {
            if (!registry.IsOnline(bridge.Id)) continue;
            await hub.Clients.Client(registry.ConnectionIdOf(bridge.Id, companyId))
                .SendAsync(BridgeProtocol.ServerToBridge.RemoveConnection,
                    new RemoveConnectionRequest(connectionId), ct);
        }
    }

    public virtual Task ClearSelectedTablesAsync(Guid connectionId, string companyId, CancellationToken ct = default) =>
        profiles.SaveSelectedTablesAsync(connectionId, companyId, [], ct);

    private async Task SendAsync(
        string signalRConnectionId, Guid bridgeId, Data.Entities.SavedConnection connection, CancellationToken ct)
    {
        // Empty selection permits metadata only; the same scope applies on both routes.
        var allowedTables = await profiles.GetSelectedTablesAsync(connection.Id, connection.CompanyId, ct);
        var route = await bridges.GetConnectionRouteAsync(connection.Id, connection.CompanyId, ct);
        route.ValidateBinding(connection);
        if (!connection.IsActive || route.ConnectionMode != ConnectionRoute.Bridge || route.BridgeId != bridgeId)
            throw new DataSourceException("Bağlantı yönlendirmesi değişti; kimlik bilgileri gönderilmedi.");
        await bridges.ValidateRouteAsync(route, connection.CompanyId, ct);

        await hub.Clients.Client(signalRConnectionId).SendAsync(
            BridgeProtocol.ServerToBridge.ConfigureConnection,
            new ConfigureConnectionRequest(
                connection.Id,
                connection.Name,
                connection.Host,
                connection.Port,
                connection.Database,
                connection.Username,
                EncryptionHelper.Decrypt(connection.EncryptedPassword),
                connection.TrustServerCertificate,
                allowedTables),
            ct);
    }
}

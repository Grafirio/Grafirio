using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

internal static class ConnectionRouteSave
{
    internal static async Task SaveAsync(
        SavedConnection connection, string? connectionMode, Guid? bridgeId, Action mutate,
        bool isNew, DataAnalysisDbContext db, BridgeStore bridges,
        BridgeConnectionSync bridgeSync, BridgeRegistry registry, ILogger logger)
    {
        ConnectionRoute? explicitRoute = connectionMode is null && bridgeId is null
            ? null : new ConnectionRoute(connectionMode ?? ConnectionRoute.Direct, bridgeId);
        if (explicitRoute is not null)
            await bridges.ValidateRouteAsync(explicitRoute, connection.CompanyId);
        var previous = await bridges.GetConnectionRouteAsync(connection.Id, connection.CompanyId);
        var recovering = previous.Pending && previous.Failed;
        var unbound = previous.Fingerprint is null || previous.Revision is null;
        if (!isNew && (recovering || unbound) && explicitRoute is null)
            throw new DataSourceException("Bağlantı hedefini açıkça seçip parolayı yeniden girin.");
        var requested = explicitRoute ?? previous with { Pending = false, Failed = false };
        await bridges.ValidateRouteAsync(requested, connection.CompanyId);
        var original = (SavedConnection)db.Entry(connection).CurrentValues.ToObject();
        var originalFingerprint = ConnectionRouteFingerprint.Create(original);
        if (!isNew && !recovering && previous.Fingerprint is not null && previous.Fingerprint != originalFingerprint)
            throw new DataSourceException("Bağlantı kaydı değişti; yeniden yükleyip kaydedin.");

        try
        {
            mutate();
            if (!isNew && (recovering || unbound || SourceChanged(original, connection, previous, requested)) &&
                original.EncryptedPassword == connection.EncryptedPassword)
                throw new DataSourceException("Bağlantı hedefi değiştiğinde parola yeniden girilmelidir.");
        }
        catch
        {
            db.Entry(connection).CurrentValues.SetValues(original);
            throw;
        }

        var revision = Guid.NewGuid().ToString("N");
        try
        {
            await bridges.ReserveConnectionRouteAsync(connection.Id, connection.CompanyId, previous, revision);
        }
        catch
        {
            db.Entry(connection).CurrentValues.SetValues(original);
            throw;
        }

        try
        {
            if (recovering)
            {
                // A failed reservation can describe an older SQL snapshot; only a fresh edit may replace it.
                var persisted = await db.SavedConnections.AsNoTracking().FirstOrDefaultAsync(value =>
                    value.Id == original.Id && value.CompanyId == original.CompanyId);
                if (persisted is null || ConnectionRouteFingerprint.Create(persisted) != originalFingerprint)
                    throw new DataSourceException("Bağlantı kaydı değişti; yeniden yükleyip kaydedin.");
            }
            connection.UpdatedAt = ConnectionRouteFingerprint.NextUpdatedAt(original);
            if (isNew) db.SavedConnections.Add(connection);
            var clearScope = !isNew && (recovering || unbound || SourceChanged(original, connection, previous, requested));
            if (clearScope)
            {
                var analyses = await db.AnalysisConfigs.Where(config => config.ConnectionId == connection.Id &&
                    config.CompanyId == connection.CompanyId && config.IsActive).ToListAsync();
                foreach (var analysis in analyses) analysis.IsActive = false;
            }
            await db.SaveChangesAsync();

            // Never publish before cleanup: a failed retry has an unknown previous source scope.
            if (!isNew)
            {
                await bridgeSync.ForgetAsync(connection.Id, connection.CompanyId, registry);
                if (clearScope)
                    await bridgeSync.ClearSelectedTablesAsync(connection.Id, connection.CompanyId);
            }
            await bridges.CompleteConnectionRouteAsync(connection.Id, connection.CompanyId, revision,
                requested with { Pending = false, Failed = false, Revision = revision,
                    Fingerprint = ConnectionRouteFingerprint.Create(connection) });
        }
        catch (Exception)
        {
            // Only restore when a fresh database read proves that the relational write did not commit.
            try
            {
                var persisted = await db.SavedConnections.AsNoTracking().FirstOrDefaultAsync(value =>
                    value.Id == original.Id && value.CompanyId == original.CompanyId);
                if (!isNew && !recovering && persisted is not null && ConnectionRouteFingerprint.Create(persisted) == originalFingerprint)
                    await bridges.CompleteConnectionRouteAsync(original.Id, original.CompanyId, revision, previous);
                else
                    await bridges.FailConnectionRouteAsync(original.Id, original.CompanyId, revision);
            }
            catch (Exception recoveryException)
            {
                logger.LogError(recoveryException, "Connection route recovery failed for {ConnectionId}; route remains blocked",
                    connection.Id);
            }
            throw;
        }
    }

    private static bool SourceChanged(SavedConnection original, SavedConnection current,
        ConnectionRoute previous, ConnectionRoute requested) =>
        original.Host != current.Host || original.Port != current.Port || original.Database != current.Database ||
        original.Username != current.Username || original.TrustServerCertificate != current.TrustServerCertificate ||
        previous.ConnectionMode != requested.ConnectionMode || previous.BridgeId != requested.BridgeId;
}
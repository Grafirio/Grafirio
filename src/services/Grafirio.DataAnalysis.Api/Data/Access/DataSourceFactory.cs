using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Data.Access;

public sealed class DataSourceFactory(
    BridgeStore bridges,
    BridgeRegistry registry,
    IHubContext<BridgeHub> hub,
    ILoggerFactory loggerFactory,
    ConnectionProfileStore profiles) : IDataSourceFactory
{
    public Task<IDataSourceSession> OpenAsync(SavedConnection connection, CancellationToken ct = default) =>
        OpenSavedAsync(connection, DataSourceTarget.DefaultConnectTimeoutSeconds, ct);

    private async Task<IDataSourceSession> OpenSavedAsync(
        SavedConnection connection, int timeoutSeconds, CancellationToken ct)
    {
        var route = await bridges.GetConnectionRouteAsync(connection.Id, connection.CompanyId, ct);
        route.ValidateBinding(connection);
        await ValidateRouteAsync(route, connection.CompanyId, ct);
        var allowedTables = await profiles.GetSelectedTablesAsync(connection.Id, connection.CompanyId, ct);
        var target = DataSourceTarget.From(connection) with
        {
            Route = route, CompanyId = connection.CompanyId, AllowedTables = allowedTables
        };
        await ValidateSavedRouteAsync(connection, route, ct);
        var session = route.ConnectionMode == ConnectionRoute.Bridge
            ? CreateBridgeSession(target, connection.Id, connection.Name, temporary: false)
            : await OpenDirectAsync(target, timeoutSeconds, ct);
        return new ConnectionRouteSession(session, token => ValidateSavedRouteAsync(connection, route, token));
    }

    private async Task ValidateSavedRouteAsync(SavedConnection connection, ConnectionRoute expected, CancellationToken ct)
    {
        var current = await bridges.GetConnectionRouteAsync(connection.Id, connection.CompanyId, ct);
        current.ValidateBinding(connection);
        if (current != expected)
            throw new DataSourceException("Bağlantı yönlendirmesi değişti; oturumu yeniden açın.");
    }

    public async Task<IDataSourceSession> OpenAsync(DataSourceTarget target, CancellationToken ct = default)
    {
        await ValidateRouteAsync(target.Route, target.CompanyId, ct);
        return target.Route.ConnectionMode == ConnectionRoute.Bridge
            ? CreateBridgeSession(target, Guid.NewGuid(), "Connection preview", temporary: true)
            : await OpenDirectAsync(target, DataSourceTarget.ProbeConnectTimeoutSeconds, ct);
    }

    private async Task ValidateRouteAsync(ConnectionRoute route, string companyId, CancellationToken ct)
    {
        await bridges.ValidateRouteAsync(route, companyId, ct);
        if (route.ConnectionMode != ConnectionRoute.Bridge) return;
        try
        {
            registry.ConnectionIdOf(route.BridgeId!.Value, companyId);
        }
        catch (BridgeUnavailableException exception)
        {
            throw new DataSourceException(exception.Message, exception);
        }
    }

    private BridgeDataSourceSession CreateBridgeSession(
        DataSourceTarget target, Guid connectionId, string name, bool temporary) =>
        new(target.Route.BridgeId!.Value, connectionId, target.CompanyId, hub, registry,
            loggerFactory.CreateLogger<BridgeDataSourceSession>(),
            new ConfigureConnectionRequest(connectionId, name, target.Host, target.Port,
                target.Database, target.Username, target.Password, target.TrustServerCertificate,
                target.AllowedTables), temporary);

    private static async Task<IDataSourceSession> OpenDirectAsync(
        DataSourceTarget target, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            return await DirectDataSourceSession.OpenAsync(target, timeoutSeconds, ct);
        }
        catch (SqlException exception)
        {
            throw new DataSourceException($"Bağlantı kurulamadı: {exception.Message}", exception);
        }
    }

    public async Task<ProbeResult> ProbeAsync(SavedConnection connection, CancellationToken ct = default)
    {
        try
        {
            await using var session = await OpenSavedAsync(connection, DataSourceTarget.ProbeConnectTimeoutSeconds, ct);
            await session.ScalarAsync<int>("SELECT 1", timeoutSeconds: DataSourceTarget.ProbeConnectTimeoutSeconds, ct: ct);
            return new ProbeResult(true, "Bağlantı başarılı");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger<DataSourceFactory>().LogWarning(exception,
                "Connection probe failed for {ConnectionId}", connection.Id);
            return new ProbeResult(false, $"Bağlantı kurulamadı: {exception.Message}");
        }
    }
}
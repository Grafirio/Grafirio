using Grafirio.Bridge.Desktop.LocalWorkspace.Data;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using Grafirio.Bridge.Desktop.LocalWorkspace.Storage;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Services;

public sealed class LocalWorkspaceService(ILocalWorkspaceStore store, ILocalDatabase database) : ILocalWorkspaceService
{
    public async Task<WorkspaceSnapshot> ReadAsync(CancellationToken cancellationToken) =>
        Snapshot(await store.ReadAsync(cancellationToken));

    public async Task<WorkspaceSnapshot> SaveConnectionAsync(LocalConnection connection, CancellationToken cancellationToken)
    {
        WorkspaceValidation.Connection(connection, requirePassword: false);
        var document = await store.UpdateAsync(current =>
        {
            var previous = current.Connections.SingleOrDefault(item => item.Id == connection.Id);
            if (connection.Id != Guid.Empty && previous is null)
                throw new ArgumentException("Yerel bağlantı bulunamadı.");
            var saved = connection with
            {
                Id = previous?.Id ?? Guid.NewGuid(),
                Password = connection.Password.Length == 0 ? previous?.Password ?? "" : connection.Password,
                AllowedTables = connection.AllowedTables.Distinct(StringComparer.Ordinal).ToArray()
            };
            WorkspaceValidation.Connection(saved);
            return current with
            {
                Connections = current.Connections.Where(item => item.Id != saved.Id).Append(saved).ToList(),
                SelectedConnectionId = saved.Id
            };
        }, cancellationToken);
        return Snapshot(document);
    }

    public async Task<WorkspaceSnapshot> DeleteConnectionAsync(Guid id, CancellationToken cancellationToken) =>
        Snapshot(await store.UpdateAsync(current =>
        {
            Find(current, id);
            return current with
            {
                Connections = current.Connections.Where(connection => connection.Id != id).ToList(),
                SelectedConnectionId = current.SelectedConnectionId == id ? null : current.SelectedConnectionId
            };
        }, cancellationToken));

    public async Task SaveSelectionAsync(WorkspaceSelection selection, CancellationToken cancellationToken)
    {
        await store.UpdateAsync(current =>
        {
            WorkspaceValidation.Selection(current, selection);
            return current with { SelectedConnectionId = selection.ConnectionId, Draft = selection.Draft };
        }, cancellationToken);
    }

    public async Task TestAsync(Guid id, CancellationToken cancellationToken) =>
        await database.TestAsync(Find(await store.ReadAsync(cancellationToken), id), cancellationToken);

    public async Task<LocalQueryResult> DiscoverAsync(Guid id, CancellationToken cancellationToken) =>
        await database.DiscoverAsync(Find(await store.ReadAsync(cancellationToken), id), cancellationToken);

    public async Task<LocalQueryResult> QueryAsync(LocalQueryRequest request, CancellationToken cancellationToken)
    {
        WorkspaceValidation.Query(request);
        return await database.QueryAsync(
            Find(await store.ReadAsync(cancellationToken), request.ConnectionId), request, cancellationToken);
    }

    private static LocalConnection Find(WorkspaceDocument document, Guid id) =>
        document.Connections.SingleOrDefault(connection => connection.Id == id)
        ?? throw new ArgumentException("Yerel bağlantı bulunamadı.");

    private static WorkspaceSnapshot Snapshot(WorkspaceDocument document) => new(
        document.Connections.Select(connection => connection.ToSummary()).ToArray(),
        document.SelectedConnectionId, document.Draft);
}
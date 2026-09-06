using Grafirio.Bridge.Desktop.LocalWorkspace.Models;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Services;

public interface ILocalWorkspaceService
{
    Task<WorkspaceSnapshot> ReadAsync(CancellationToken cancellationToken);
    Task<WorkspaceSnapshot> SaveConnectionAsync(LocalConnection connection, CancellationToken cancellationToken);
    Task<WorkspaceSnapshot> DeleteConnectionAsync(Guid id, CancellationToken cancellationToken);
    Task SaveSelectionAsync(WorkspaceSelection selection, CancellationToken cancellationToken);
    Task TestAsync(Guid id, CancellationToken cancellationToken);
    Task<LocalQueryResult> DiscoverAsync(Guid id, CancellationToken cancellationToken);
    Task<LocalQueryResult> QueryAsync(LocalQueryRequest request, CancellationToken cancellationToken);
}
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Data;

public interface ILocalDatabase
{
    Task TestAsync(LocalConnection connection, CancellationToken cancellationToken);
    Task<LocalQueryResult> DiscoverAsync(LocalConnection connection, CancellationToken cancellationToken);
    Task<LocalQueryResult> QueryAsync(LocalConnection connection, LocalQueryRequest request, CancellationToken cancellationToken);
}
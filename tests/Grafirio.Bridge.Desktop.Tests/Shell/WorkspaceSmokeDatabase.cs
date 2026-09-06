using Grafirio.Bridge.Desktop.LocalWorkspace.Data;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

internal sealed class WorkspaceSmokeDatabase : ILocalDatabase
{
    private const string UnexpectedAccess = "The UI smoke test must never access a real database.";

    public Task TestAsync(LocalConnection connection, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(UnexpectedAccess);

    public Task<LocalQueryResult> DiscoverAsync(LocalConnection connection, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(UnexpectedAccess);

    public Task<LocalQueryResult> QueryAsync(
        LocalConnection connection, LocalQueryRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(UnexpectedAccess);
}
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using Grafirio.Bridge.Desktop.LocalWorkspace.Storage;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

internal sealed class WorkspaceSmokeStore(string draft) : ILocalWorkspaceStore
{
    public WorkspaceDocument Document { get; private set; } = new() { Draft = draft };
    public int ReadCount { get; private set; }
    public int UpdateCount { get; private set; }

    public Task<WorkspaceDocument> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        return Task.FromResult(Document);
    }

    public Task<WorkspaceDocument> UpdateAsync(
        Func<WorkspaceDocument, WorkspaceDocument> update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Document = update(Document);
        UpdateCount++;
        return Task.FromResult(Document);
    }
}
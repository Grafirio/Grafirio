using Grafirio.Bridge.Desktop.LocalWorkspace.Models;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Storage;

public interface ILocalWorkspaceStore
{
    Task<WorkspaceDocument> ReadAsync(CancellationToken cancellationToken);
    Task<WorkspaceDocument> UpdateAsync(
        Func<WorkspaceDocument, WorkspaceDocument> update, CancellationToken cancellationToken);
}
namespace Grafirio.Bridge.Desktop.LocalWorkspace.Models;

public sealed record WorkspaceSnapshot(
    IReadOnlyList<LocalConnectionSummary> Connections, Guid? SelectedConnectionId, string Draft);
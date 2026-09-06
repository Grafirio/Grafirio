namespace Grafirio.Bridge.Desktop.LocalWorkspace.Models;

public sealed record WorkspaceDocument
{
    public int Version { get; init; } = 1;
    public List<LocalConnection> Connections { get; init; } = [];
    public Guid? SelectedConnectionId { get; init; }
    public string Draft { get; init; } = "SELECT 1 AS Result";
}
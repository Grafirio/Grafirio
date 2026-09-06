namespace Grafirio.Bridge.Desktop.LocalWorkspace.Models;

public sealed record LocalQueryRequest(Guid ConnectionId, string Sql, int MaxRows, int TimeoutSeconds);
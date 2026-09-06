using Grafirio.DataAnalysis.Api.Data.Access;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

public sealed record ConnectionPreviewRequest(
    string Host,
    int Port,
    string? Database,
    string Username,
    string? Password,
    bool TrustServerCertificate = true,
    string? ConnectionMode = null,
    Guid? BridgeId = null,
    Guid? ConnectionId = null)
{
    public ConnectionRoute Route => new(ConnectionMode ?? ConnectionRoute.Direct, BridgeId);
}
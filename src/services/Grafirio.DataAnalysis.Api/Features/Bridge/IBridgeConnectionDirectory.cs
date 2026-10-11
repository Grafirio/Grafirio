namespace Grafirio.DataAnalysis.Api.Features.Bridge;

public sealed record BridgeConnectionLease(string ConnectionId, string CompanyId, string Version);

public interface IBridgeConnectionDirectory
{
    Task SetAsync(Guid bridgeId, BridgeConnectionLease lease, CancellationToken ct);
    Task<BridgeConnectionLease?> FindAsync(Guid bridgeId, CancellationToken ct);
    Task<bool> RefreshAsync(Guid bridgeId, BridgeConnectionLease lease, CancellationToken ct);
    Task RemoveAsync(Guid bridgeId, BridgeConnectionLease lease, CancellationToken ct);
}

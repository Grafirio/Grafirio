using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.DataAnalysis.Tests;

public sealed class BridgeConnectionDirectoryTests
{
    [Fact]
    public async Task AnotherReplicaResolvesOnlyTheAuthorizedCompany()
    {
        var directory = new MemoryDirectory();
        var first = Registry(directory);
        var second = Registry(directory);
        var bridge = Guid.NewGuid();
        await first.AttachAsync(bridge, "socket-1", "company", "1", default);
        Assert.Equal("socket-1", await second.ConnectionIdOfAsync(bridge, "company"));
        await Assert.ThrowsAsync<BridgeUnavailableException>(() => second.ConnectionIdOfAsync(bridge, "other-company"));
        directory.Leases.Clear();
        await Assert.ThrowsAsync<BridgeUnavailableException>(() => first.ConnectionIdOfAsync(bridge, "company"));
    }

    [Fact]
    public async Task OldSocketCannotRefreshOrRemoveTheNewLease()
    {
        var directory = new MemoryDirectory();
        var first = Registry(directory);
        var second = Registry(directory);
        var bridge = Guid.NewGuid();
        await first.AttachAsync(bridge, "old", "company", "1", default);
        await second.AttachAsync(bridge, "new", "company", "1", default);
        Assert.False(await first.RefreshAsync(bridge, "old", default));
        await first.DetachAsync(bridge, "old", default);
        Assert.Equal("new", await first.ConnectionIdOfAsync(bridge, "company"));
        Assert.True(await second.RefreshAsync(bridge, "new", default));
        await second.DetachAsync(bridge, "new", default);
        Assert.False(await first.IsOnlineAsync(bridge, "company"));
    }

    private static BridgeRegistry Registry(IBridgeConnectionDirectory directory) =>
        new(new InProcessBridgeResponseBus(), NullLogger<BridgeRegistry>.Instance, directory);

    private sealed class MemoryDirectory : IBridgeConnectionDirectory
    {
        public Dictionary<Guid, BridgeConnectionLease> Leases { get; } = [];
        public Task SetAsync(Guid bridgeId, BridgeConnectionLease lease, CancellationToken ct)
        { Leases[bridgeId] = lease; return Task.CompletedTask; }
        public Task<BridgeConnectionLease?> FindAsync(Guid bridgeId, CancellationToken ct) => Task.FromResult(Leases.GetValueOrDefault(bridgeId));
        public Task<bool> RefreshAsync(Guid bridgeId, BridgeConnectionLease lease, CancellationToken ct) =>
            Task.FromResult(Leases.GetValueOrDefault(bridgeId) == lease);
        public Task RemoveAsync(Guid bridgeId, BridgeConnectionLease lease, CancellationToken ct)
        {
            if (Leases.GetValueOrDefault(bridgeId) == lease) Leases.Remove(bridgeId);
            return Task.CompletedTask;
        }
    }
}

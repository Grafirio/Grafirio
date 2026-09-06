using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class BridgeConnectionSyncTests
{
    [Fact]
    public async Task ForgetRemovesOldAndCurrentBridgeCopiesBeforeNewConfiguration()
    {
        var harness = new BridgeRoutingHarness();
        var clients = new Mock<IHubClients>();
        clients.Setup(value => value.Client(It.IsAny<string>())).Returns(harness.Client.Object);
        harness.Hub.SetupGet(value => value.Clients).Returns(clients.Object);
        harness.Store.Setup(value => value.ListAsync(BridgeRoutingHarness.Company, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisteredBridge[]
            {
                new(BridgeRoutingHarness.OtherBridge, BridgeRoutingHarness.Company, "old", "pc", "1", DateTime.UtcNow, null),
                new(BridgeRoutingHarness.SelectedBridge, BridgeRoutingHarness.Company, "new", "pc", "1", DateTime.UtcNow, null)
            });
        await using var services = CreateServices();
        await SeedAsync(services, harness);
        var sync = Sync(harness, services);
        await sync.ForgetAsync(harness.Saved.Id, BridgeRoutingHarness.Company, harness.Registry);
        await sync.SyncOneAsync(harness.Saved.Id, BridgeRoutingHarness.Company, harness.Registry);
        Assert.Equal([BridgeProtocol.ServerToBridge.RemoveConnection, BridgeProtocol.ServerToBridge.RemoveConnection,
            BridgeProtocol.ServerToBridge.ConfigureConnection], harness.Messages.Select(value => value.Method));
        clients.Verify(value => value.Client("other-client"), Times.Once);
        clients.Verify(value => value.Client("selected-client"), Times.Exactly(2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingOrMismatchedRouteNeverSyncsAndReconnectRemovesStaleCredentials(bool pending)
    {
        var harness = new BridgeRoutingHarness();
        await using var services = CreateServices();
        await SeedAsync(services, harness);
        harness.Route = pending ? harness.Route with { Pending = true } : harness.Route with { Fingerprint = "stale" };
        var sync = Sync(harness, services);
        await Assert.ThrowsAsync<DataSourceException>(() => sync.SyncOneAsync(
            harness.Saved.Id, BridgeRoutingHarness.Company, harness.Registry));
        Assert.Empty(harness.Messages);
        await sync.SyncAllAsync(BridgeRoutingHarness.SelectedBridge, BridgeRoutingHarness.Company, "selected-client");
        Assert.Equal(BridgeProtocol.ServerToBridge.RemoveConnection, Assert.Single(harness.Messages).Method);
    }

    [Fact]
    public async Task SaveSyncTargetsOnlySelectedBridgeWithCurrentCredentials()
    {
        var harness = new BridgeRoutingHarness();
        await using var services = CreateServices();
        await SeedAsync(services, harness);
        await Sync(harness, services).SyncOneAsync(harness.Saved.Id, BridgeRoutingHarness.Company, harness.Registry);
        var message = Assert.Single(harness.Messages);
        var configuration = Assert.IsType<ConfigureConnectionRequest>(message.Payload);
        Assert.Equal(harness.Saved.Id, configuration.ConnectionId);
        Assert.Equal("test-only-sync-password", configuration.Password);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveSyncDoesNotUseAnotherOnlineBridgeWhenSelectedIsOfflineOrDirect(bool direct)
    {
        var harness = new BridgeRoutingHarness();
        if (direct) harness.Route = new ConnectionRoute();
        else harness.Registry.Detach(BridgeRoutingHarness.SelectedBridge, "selected-client");
        await using var services = CreateServices();
        await SeedAsync(services, harness);
        await Sync(harness, services).SyncOneAsync(harness.Saved.Id, BridgeRoutingHarness.Company, harness.Registry);
        Assert.Empty(harness.Messages);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("other")]
    [InlineData("deleted")]
    public async Task ReconnectRemovesUnassignedAndDeletedCredentials(string scenario)
    {
        var harness = new BridgeRoutingHarness();
        if (scenario == "direct") harness.Route = new ConnectionRoute();
        if (scenario == "other") harness.Route = new ConnectionRoute(ConnectionRoute.Bridge, BridgeRoutingHarness.OtherBridge);
        if (scenario == "deleted") harness.Saved.IsActive = false;
        await using var services = CreateServices();
        await SeedAsync(services, harness);
        await Sync(harness, services).SyncAllAsync(BridgeRoutingHarness.SelectedBridge,
            BridgeRoutingHarness.Company, "selected-client");
        var message = Assert.Single(harness.Messages);
        Assert.Equal(BridgeProtocol.ServerToBridge.RemoveConnection, message.Method);
        Assert.Equal(harness.Saved.Id, Assert.IsType<RemoveConnectionRequest>(message.Payload).ConnectionId);
    }

    private static ServiceProvider CreateServices()
    {
        var name = Guid.NewGuid().ToString();
        return new ServiceCollection().AddDbContext<DataAnalysisDbContext>(options => options.UseInMemoryDatabase(name))
            .BuildServiceProvider();
    }

    private static async Task SeedAsync(ServiceProvider services, BridgeRoutingHarness harness)
    {
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", "test-only-connection-encryption-key");
        harness.Saved.EncryptedPassword = EncryptionHelper.Encrypt("test-only-sync-password");
        harness.BindRoute();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();
        db.Add(harness.Saved);
        await db.SaveChangesAsync();
    }

    private static BridgeConnectionSync Sync(BridgeRoutingHarness harness, ServiceProvider services) =>
        new(harness.Hub.Object, harness.Store.Object, harness.Profiles,
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BridgeConnectionSync>.Instance);
}
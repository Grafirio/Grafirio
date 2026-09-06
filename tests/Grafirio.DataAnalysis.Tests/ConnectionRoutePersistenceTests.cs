using Grafirio.DataAnalysis.Api.Data.Access;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class ConnectionRoutePersistenceTests
{
    [Fact]
    public async Task OmittedUpdateRoutePreservesSavedBridge()
    {
        var harness = new BridgeRoutingHarness();
        await harness.Store.Object.SetRequestedRouteAsync(harness.Saved.Id, BridgeRoutingHarness.Company, null, null);
        harness.Store.Verify(store => store.SaveConnectionRouteAsync(harness.Saved.Id, BridgeRoutingHarness.Company,
            harness.Route, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExplicitDirectIsNotInferredFromCompanyBridgePresence()
    {
        var harness = new BridgeRoutingHarness();
        await harness.Store.Object.SetRequestedRouteAsync(harness.Saved.Id, BridgeRoutingHarness.Company,
            ConnectionRoute.Direct, null);
        harness.Store.Verify(store => store.SaveConnectionRouteAsync(harness.Saved.Id, BridgeRoutingHarness.Company,
            new ConnectionRoute(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForeignBridgeCannotBePersisted()
    {
        var harness = new BridgeRoutingHarness();
        harness.Store.CallBase = true;
        await Assert.ThrowsAsync<DataSourceException>(() => harness.Store.Object.SaveConnectionRouteAsync(
            harness.Saved.Id, "another-company", harness.Route));
    }

    [Fact]
    public async Task MissingBridgeIdCannotBePersisted()
    {
        var harness = new BridgeRoutingHarness();
        harness.Store.CallBase = true;
        await Assert.ThrowsAsync<DataSourceException>(() => harness.Store.Object.SaveConnectionRouteAsync(
            harness.Saved.Id, BridgeRoutingHarness.Company, new ConnectionRoute(ConnectionRoute.Bridge)));
    }
}
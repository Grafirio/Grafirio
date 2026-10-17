using System.Security.Claims;
using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class BridgeTelemetryHubTests
{
    private static (BridgeHub Hub, BridgeRegistry Registry, Guid Id) Fixture(string company = "firm-a", string socket = "socket")
    {
        var id = Guid.NewGuid();
        var registry = new BridgeRegistry(new InProcessBridgeResponseBus(), NullLogger<BridgeRegistry>.Instance);
        registry.Attach(id, "socket", "firm-a", "1");
        var context = new Mock<HubCallerContext>();
        context.SetupGet(value => value.ConnectionId).Returns(socket);
        context.SetupGet(value => value.User).Returns(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(BridgeAuthentication.BridgeIdClaim, id.ToString()), new Claim(BridgeAuthentication.CompanyIdClaim, company)], "test")));
        var hub = new BridgeHub(registry, Mock.Of<IBridgePresence>(), NullLogger<BridgeHub>.Instance) { Context = context.Object };
        return (hub, registry, id);
    }
    private static BridgeTelemetryBatch Batch() => new([new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, Guid.NewGuid().ToString("N"), "1.0.0", "bridge.connected", null)], 0);

    [Fact]
    public async Task WrongCompanyOrStaleSocketCannotSubmitTelemetry()
    {
        var wrongCompany = Fixture("firm-b");
        await Assert.ThrowsAsync<BridgeUnavailableException>(() => wrongCompany.Hub.PushTelemetry(Batch()));
        var stale = Fixture(socket: "old-socket");
        await Assert.ThrowsAsync<HubException>(() => stale.Hub.PushTelemetry(Batch()));
    }

    [Fact]
    public async Task DisabledCollectorDoesNotAcknowledgeOrDiscardEdgeOutbox()
    {
        var fixture = Fixture();
        Assert.False(await fixture.Hub.PushTelemetry(Batch()));
        await Assert.ThrowsAsync<HubException>(() => fixture.Hub.PushTelemetry(new([Batch().Events[0] with { Name = "SELECT customer-secret" }], 0)));
    }
}

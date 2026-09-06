using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class BridgeConnectionRoutingTests
{
    [Theory]
    [InlineData("pending")]
    [InlineData("missing")]
    [InlineData("password")]
    [InlineData("host")]
    [InlineData("revision")]
    public async Task UnboundRoutesFailBeforeDecryptingOrOpeningAnyTransport(string scenario)
    {
        var harness = new BridgeRoutingHarness();
        if (scenario == "pending") harness.Route = harness.Route with { Pending = true };
        if (scenario == "missing") harness.Route = new ConnectionRoute();
        if (scenario == "password") harness.Saved.EncryptedPassword = "changed-ciphertext";
        if (scenario == "host") harness.Saved.Host = "changed-host";
        if (scenario == "revision") harness.Saved.UpdatedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<DataSourceException>(() => harness.Factory.OpenAsync(harness.Saved));
        Assert.False((await harness.Factory.ProbeAsync(harness.Saved)).Success);
        Assert.Empty(harness.Messages);
    }

    [Fact]
    public async Task SessionOpenedBeforeReservationCannotSendStaleCredentials()
    {
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", "test-only-connection-encryption-key");
        var harness = new BridgeRoutingHarness();
        harness.Saved.EncryptedPassword = EncryptionHelper.Encrypt("test-only-password");
        harness.BindRoute();
        await using var session = await harness.Factory.OpenAsync(harness.Saved);
        harness.Route = harness.Route with { Pending = true };
        await Assert.ThrowsAsync<DataSourceException>(() => session.ScalarAsync<int>("SELECT 1"));
        Assert.Empty(harness.Messages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectedOfflineBridgeNeverFallsBackToOtherBridgeOrCloud(bool saved)
    {
        var harness = new BridgeRoutingHarness();
        harness.Registry.Detach(BridgeRoutingHarness.SelectedBridge, "selected-client");
        var exception = await Assert.ThrowsAsync<DataSourceException>(() => saved
            ? harness.Factory.OpenAsync(harness.Saved) : harness.Factory.OpenAsync(harness.Preview));
        Assert.Contains("çevrimdışı", exception.Message);
        Assert.Empty(harness.Messages);
        var probe = await harness.Factory.ProbeAsync(harness.Saved);
        Assert.False(probe.Success);
        Assert.Contains("çevrimdışı", probe.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForeignOrRevokedBridgeIsRejectedBeforeCredentialsAreRead(bool foreign)
    {
        var harness = new BridgeRoutingHarness();
        harness.Store.Setup(store => store.FindAsync(BridgeRoutingHarness.SelectedBridge, It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreign
                ? new RegisteredBridge(BridgeRoutingHarness.SelectedBridge, "another-company", "", "", "1", DateTime.UtcNow, null)
                : null);
        await Assert.ThrowsAsync<DataSourceException>(() => harness.Factory.OpenAsync(harness.Saved));
        await Assert.ThrowsAsync<DataSourceException>(() => harness.Factory.OpenAsync(harness.Preview));
        Assert.Empty(harness.Messages);
    }

    [Fact]
    public async Task LiveChannelOwnershipIsCheckedIndependentlyOfRegistration()
    {
        var harness = new BridgeRoutingHarness();
        harness.Registry.Attach(BridgeRoutingHarness.SelectedBridge, "selected-client", "another-company", "1");
        var error = await Assert.ThrowsAsync<DataSourceException>(() => harness.Factory.OpenAsync(harness.Saved));
        Assert.Contains("şirkete ait değil", error.Message);
        Assert.Empty(harness.Messages);
    }

    [Theory]
    [InlineData("bridge", null)]
    [InlineData("auto", null)]
    [InlineData("direct", "33333333-3333-3333-3333-333333333333")]
    public async Task InvalidRoutesCannotBecomeDirect(string mode, string? id)
    {
        var harness = new BridgeRoutingHarness { Route = new ConnectionRoute(mode, id is null ? null : Guid.Parse(id)) };
        await Assert.ThrowsAsync<DataSourceException>(() => harness.Factory.OpenAsync(harness.Preview));
        Assert.Empty(harness.Messages);
    }

    [Fact]
    public async Task PreviewConfiguresExactBridgeBeforeQueryAndRemovesTemporaryCredentials()
    {
        var harness = new BridgeRoutingHarness();
        await using (var session = await harness.Factory.OpenAsync(harness.Preview))
        {
            Assert.Equal(1, await session.ScalarAsync<int>("SELECT 1"));
        }
        Assert.Equal([BridgeProtocol.ServerToBridge.ConfigureConnection, BridgeProtocol.ServerToBridge.ExecuteQuery,
            BridgeProtocol.ServerToBridge.RemoveConnection], harness.Messages.Select(message => message.Method));
        var configuration = Assert.IsType<ConfigureConnectionRequest>(harness.Messages[0].Payload);
        var query = Assert.IsType<ExecuteQueryRequest>(harness.Messages[1].Payload);
        var removal = Assert.IsType<RemoveConnectionRequest>(harness.Messages[2].Payload);
        Assert.Equal(configuration.ConnectionId, query.ConnectionId);
        Assert.Equal(query.ConnectionId, removal.ConnectionId);
        Assert.NotEqual(harness.Saved.Id, query.ConnectionId);
        Assert.Equal(harness.Preview.Password, configuration.Password);
        Assert.Empty(configuration.AllowedTables);
    }

    [Fact]
    public async Task SavedSchemaAndRuntimeQueriesConfigureCurrentCredentialsAndTableScope()
    {
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", "test-only-connection-encryption-key");
        var harness = new BridgeRoutingHarness();
        harness.Saved.EncryptedPassword = EncryptionHelper.Encrypt("test-only-updated-password");
        harness.BindRoute();
        await using var session = await harness.Factory.OpenAsync(harness.Saved);
        await session.QueryRowsAsync("SELECT name FROM sys.tables");
        await session.QueryRowsAsync("SELECT Id FROM dbo.Orders");
        Assert.Equal(4, harness.Messages.Count);
        var configurations = harness.Messages.Where(message => message.Payload is ConfigureConnectionRequest)
            .Select(message => (ConfigureConnectionRequest)message.Payload).ToList();
        Assert.All(configurations, configuration =>
        {
            Assert.Equal(harness.Saved.Id, configuration.ConnectionId);
            Assert.Equal("test-only-updated-password", configuration.Password);
            Assert.Equal(["dbo.Orders"], configuration.AllowedTables);
        });
    }

    [Fact]
    public async Task FailedCredentialSyncPreventsExecution()
    {
        var harness = new BridgeRoutingHarness { FailConfiguration = true };
        await using var session = await harness.Factory.OpenAsync(harness.Preview);
        var error = await Assert.ThrowsAsync<DataSourceException>(() => session.ScalarAsync<int>("SELECT 1"));
        Assert.Contains("gönderilemedi", error.Message);
        Assert.DoesNotContain(harness.Messages, message => message.Payload is ExecuteQueryRequest);
    }

    [Fact]
    public async Task MissingBridgeResponseHasBoundedClearFailure()
    {
        var harness = new BridgeRoutingHarness { CompleteQueries = false };
        await using var session = await harness.Factory.OpenAsync(harness.Preview);
        var error = await Assert.ThrowsAsync<DataSourceException>(() => session.ScalarAsync<int>("SELECT 1", timeoutSeconds: 0));
        Assert.Contains("zaman aşımı", error.Message);
    }

    [Fact]
    public async Task DirectModeDoesNotSelectAnOnlineCompanyBridge()
    {
        var harness = new BridgeRoutingHarness { Route = new ConnectionRoute() };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Factory.OpenAsync(harness.Preview, cancellation.Token));
        Assert.Empty(harness.Messages);
        harness.Store.Verify(store => store.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
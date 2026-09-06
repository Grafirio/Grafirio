using Grafirio.Bridge.Desktop.Cloud;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge.Desktop.Tests.Cloud;

public sealed class DesktopConnectionContextTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ContextWaitsForFirstReadyAndReturnsOnlyTheRunningHostId()
    {
        var sessions = SignedIn();
        var host = new ContextBridgeHost();
        await using var controller = Create(sessions, host);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var connection = controller.ConnectAsync(sessions.Current!, deadline.Token);
        await host.Started.Task.WaitAsync(deadline.Token);
        var context = controller.GetConnectionContextAsync(sessions.Current!, deadline.Token);
        Assert.False(connection.IsCompleted);
        Assert.False(context.IsCompleted);
        host.Ready();
        await connection;
        Assert.Equal(host.BridgeId, await context);
        Assert.Equal(1, host.Starts);
    }

    [Fact]
    public async Task ContextCanStartBridgeAfterReauthenticationWithoutSeparateConnectCall()
    {
        var sessions = SignedIn();
        var host = new ContextBridgeHost();
        host.Ready();
        await using var controller = Create(sessions, host);
        Assert.Equal(host.BridgeId,
            await controller.GetConnectionContextAsync(sessions.Current!, CancellationToken.None));
    }

    [Fact]
    public async Task InitialDisconnectedHostIsStoppedAndNextConnectRetriesWithNewHost()
    {
        var sessions = SignedIn();
        var failedHost = new ContextBridgeHost();
        failedHost.Ready();
        failedHost.IsConnected = false;
        var readyHost = new ContextBridgeHost();
        readyHost.Ready();
        var hosts = new Queue<ContextBridgeHost>([failedHost, readyHost]);
        await using var controller = new DesktopBridgeController(sessions, Options.Create(new BridgeOptions()),
            NullLoggerFactory.Instance, (_, _, _) => hosts.Dequeue());
        await Assert.ThrowsAsync<DesktopBridgeException>(() =>
            controller.ConnectAsync(sessions.Current!, CancellationToken.None));
        Assert.True(failedHost.Disposed);
        await controller.ConnectAsync(sessions.Current!, CancellationToken.None);
        Assert.Equal(readyHost.BridgeId,
            await controller.GetConnectionContextAsync(sessions.Current!, CancellationToken.None));
        Assert.Empty(hosts);
    }

    [Fact]
    public void HostDisplayTracksDisconnectionAfterTheFirstSuccessfulAttempt()
    {
        var display = new DesktopBridgeDisplay(NullLogger<DesktopBridgeDisplay>.Instance);
        display.ShowStatus(BridgeStatus.Connected);
        Assert.True(display.IsConnected);
        Assert.True(display.ConnectionAttempt.IsCompletedSuccessfully);
        display.ShowStatus(BridgeStatus.Disconnected);
        Assert.False(display.IsConnected);
        display.ShowStatus(BridgeStatus.Connected);
        Assert.True(display.IsConnected);
        display.ShowStatus(BridgeStatus.Stopped);
        Assert.False(display.IsConnected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignOutOrIdentitySwitchDuringStartupCannotReturnStaleContext(bool switchIdentity)
    {
        var sessions = SignedIn();
        var host = new ContextBridgeHost();
        await using var controller = Create(sessions, host);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var context = controller.GetConnectionContextAsync(sessions.Current!, deadline.Token);
        await host.Started.Task.WaitAsync(deadline.Token);
        sessions.Set(switchIdentity ? ContextSessionManager.CreateSession("other-user") : null);
        host.Ready();
        await Assert.ThrowsAsync<DesktopBridgeException>(() => context);
        Assert.True(host.Disposed);
    }

    [Fact]
    public async Task StaleCompanyContextIsRejectedWithoutStartingAHost()
    {
        var sessions = SignedIn();
        var stale = ContextSessionManager.CreateSession(company: "other-company");
        var host = new ContextBridgeHost();
        await using var controller = Create(sessions, host);
        await Assert.ThrowsAsync<DesktopBridgeException>(() =>
            controller.GetConnectionContextAsync(stale, CancellationToken.None));
        Assert.Equal(0, host.Starts);
    }

    [Fact]
    public async Task DisconnectedHostNeverProducesContext()
    {
        var sessions = SignedIn();
        var host = new ContextBridgeHost();
        host.Ready();
        await using var controller = Create(sessions, host);
        await controller.ConnectAsync(sessions.Current!, CancellationToken.None);
        host.IsConnected = false;
        await Assert.ThrowsAsync<DesktopBridgeException>(() =>
            controller.GetConnectionContextAsync(sessions.Current!, CancellationToken.None));
        Assert.True(host.Disposed);
    }

    [Fact]
    public async Task StopCancelsPendingContextAndDoesNotRestartSignedOutHost()
    {
        var sessions = SignedIn();
        var session = sessions.Current!;
        var host = new ContextBridgeHost();
        await using var controller = Create(sessions, host);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var context = controller.GetConnectionContextAsync(session, deadline.Token);
        await host.Started.Task.WaitAsync(deadline.Token);
        sessions.Set(null);
        await controller.StopAsync(deadline.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context);
        await Assert.ThrowsAsync<DesktopBridgeException>(() =>
            controller.GetConnectionContextAsync(session, deadline.Token));
        Assert.Equal(1, host.Starts);
        Assert.True(host.Disposed);
    }

    [Fact]
    public async Task CancellationWhileQueuedDoesNotInterruptActiveConnection()
    {
        var sessions = SignedIn();
        var host = new ContextBridgeHost();
        await using var controller = Create(sessions, host);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var connection = controller.ConnectAsync(sessions.Current!, deadline.Token);
        await host.Started.Task.WaitAsync(deadline.Token);
        using var cancellation = new CancellationTokenSource();
        var context = controller.GetConnectionContextAsync(sessions.Current!, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context);
        Assert.False(host.Disposed);
        host.Ready();
        await connection;
    }

    [Fact]
    public async Task ExpiredOrMissingExpirySessionCannotStartHost()
    {
        foreach (var expiry in new DateTimeOffset?[] { null, DateTimeOffset.UtcNow.AddMinutes(-1) })
        {
            var sessions = SignedIn();
            sessions.Set(sessions.Current! with { ExpiresAt = expiry });
            var host = new ContextBridgeHost();
            await using var controller = Create(sessions, host);
            await Assert.ThrowsAsync<DesktopBridgeException>(() =>
                controller.GetConnectionContextAsync(sessions.Current!, CancellationToken.None));
            Assert.Equal(0, host.Starts);
        }
    }

    private static ContextSessionManager SignedIn()
    {
        var sessions = new ContextSessionManager();
        sessions.Set(ContextSessionManager.CreateSession());
        return sessions;
    }

    private static DesktopBridgeController Create(ContextSessionManager sessions, ContextBridgeHost host) =>
        new(sessions, Options.Create(new BridgeOptions()), NullLoggerFactory.Instance, (_, _, _) => host);
}
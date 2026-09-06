using System.Text.Json;
using System.Text.Json.Serialization;
using Grafirio.Bridge.Desktop.Shell;
using Grafirio.Bridge.Desktop.Tests.Cloud;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

public sealed class ConnectionContextRequestTests
{
    [Fact]
    public async Task SignedOutRequestNeverCallsController()
    {
        var sessions = new ContextSessionManager();
        var bridge = new ContextBridgeController();
        var response = await ConnectionContextRequest.ResolveAsync(Guid.NewGuid().ToString(), sessions,
            bridge, CancellationToken.None);
        Assert.Equal("signedOut", response.Error);
        Assert.Equal(1, sessions.GetCount);
        Assert.Equal(0, bridge.ContextCalls);
        Assert.Null(response.BridgeId);
    }

    [Fact]
    public async Task GetAsyncValidatesSessionBeforeControllerAndResponseContainsNoSecrets()
    {
        var sessions = new ContextSessionManager();
        var session = ContextSessionManager.CreateSession();
        sessions.GetSession = _ =>
        {
            sessions.Set(session);
            return Task.FromResult<UserSession?>(session);
        };
        var bridge = new ContextBridgeController();
        bridge.Resolve = (validated, _) =>
        {
            Assert.Equal(1, sessions.GetCount);
            Assert.Same(session, validated);
            return Task.FromResult(bridge.BridgeId);
        };
        var requestId = Guid.NewGuid().ToString();
        var response = await ConnectionContextRequest.ResolveAsync(requestId, sessions, bridge, CancellationToken.None);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        using var document = JsonDocument.Parse(json);
        Assert.Equal(3, document.RootElement.EnumerateObject().Count());
        Assert.Equal("connectionContext", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(requestId, document.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(bridge.BridgeId, document.RootElement.GetProperty("bridgeId").GetGuid());
        Assert.DoesNotContain(session.AccessToken, json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedSessionCannotReceivePreviousContext(bool signedOut)
    {
        var sessions = new ContextSessionManager();
        sessions.Set(ContextSessionManager.CreateSession());
        var bridge = new ContextBridgeController();
        bridge.Resolve = (_, _) =>
        {
            sessions.Set(signedOut ? null : ContextSessionManager.CreateSession("other-user"));
            return Task.FromResult(bridge.BridgeId);
        };
        var response = await ConnectionContextRequest.ResolveAsync(Guid.NewGuid().ToString(), sessions,
            bridge, CancellationToken.None);
        Assert.Equal("signedOut", response.Error);
        Assert.Null(response.BridgeId);
    }

    [Fact]
    public async Task ExpiredSessionCannotReachController()
    {
        var sessions = new ContextSessionManager();
        sessions.Set(ContextSessionManager.CreateSession() with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        var bridge = new ContextBridgeController();
        var response = await ConnectionContextRequest.ResolveAsync(Guid.NewGuid().ToString(), sessions,
            bridge, CancellationToken.None);
        Assert.Equal("signedOut", response.Error);
        Assert.Equal(0, bridge.ContextCalls);
    }

    [Fact]
    public async Task CancelledValidationDoesNotCallController()
    {
        var sessions = new ContextSessionManager();
        sessions.Set(ContextSessionManager.CreateSession());
        var bridge = new ContextBridgeController();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConnectionContextRequest.ResolveAsync(
            Guid.NewGuid().ToString(), sessions, bridge, cancellation.Token));
        Assert.Equal(0, bridge.ContextCalls);
        Assert.Equal(TimeSpan.FromSeconds(20), ConnectionContextRequest.Timeout);
    }
}
using Grafirio.Bridge.Desktop.Cloud;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

internal sealed class ContextBridgeController : IDesktopBridgeController
{
    public event Action<BridgeStatus, string?>? Changed;
    public Guid BridgeId { get; } = Guid.NewGuid();
    public int ContextCalls { get; private set; }
    public Func<UserSession, CancellationToken, Task<Guid>>? Resolve { get; set; }

    public Task<Guid> GetConnectionContextAsync(UserSession session, CancellationToken cancellationToken)
    {
        ContextCalls++;
        return Resolve?.Invoke(session, cancellationToken) ?? Task.FromResult(BridgeId);
    }

    public Task ConnectAsync(UserSession session, CancellationToken cancellationToken)
    {
        Changed?.Invoke(BridgeStatus.Connected, null);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Changed?.Invoke(BridgeStatus.Stopped, null);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
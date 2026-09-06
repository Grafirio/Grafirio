using Grafirio.Bridge.Desktop.Cloud;

namespace Grafirio.Bridge.Desktop.Tests.Cloud;

internal sealed class ContextBridgeHost : IDesktopBridgeHost
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Guid? BridgeId { get; } = Guid.NewGuid();
    public bool IsConnected { get; set; }
    public bool Disposed { get; private set; }
    public int Starts { get; private set; }

    public void Ready()
    {
        IsConnected = true;
        _ready.TrySetResult();
    }

    public async Task StartAsync(UserSession session, CancellationToken cancellationToken)
    {
        Starts++;
        Started.TrySetResult();
        await _ready.Task.WaitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
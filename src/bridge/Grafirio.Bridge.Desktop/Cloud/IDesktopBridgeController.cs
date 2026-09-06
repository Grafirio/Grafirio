namespace Grafirio.Bridge.Desktop.Cloud;

public interface IDesktopBridgeController : IAsyncDisposable
{
    event Action<BridgeStatus, string?>? Changed;

    Task ConnectAsync(UserSession session, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
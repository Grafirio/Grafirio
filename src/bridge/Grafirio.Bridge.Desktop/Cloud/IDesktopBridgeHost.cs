namespace Grafirio.Bridge.Desktop.Cloud;

internal interface IDesktopBridgeHost : IAsyncDisposable
{
    Guid? BridgeId { get; }
    bool IsConnected { get; }
    Task StartAsync(UserSession session, CancellationToken cancellationToken);
}
namespace Grafirio.Bridge.Desktop.Cloud;

internal sealed class DesktopBridgeDisplay(ILogger<DesktopBridgeDisplay> logger) : IBridgeDisplay
{
    private readonly TaskCompletionSource<bool> _connectionAttempt =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<BridgeStatus, string?>? Changed;

    public Task<bool> ConnectionAttempt => _connectionAttempt.Task;

    public void ShowDeviceCode(DeviceCodePrompt prompt) =>
        throw new DesktopBridgeException("Bulut bağlantısı için masaüstü oturumuyla yeniden giriş yapın.");

    public void ShowStatus(BridgeStatus status, string? detail = null)
    {
        // Core details may contain server bodies; never forward them to the desktop UI.
        Publish(status, status switch
        {
            BridgeStatus.EnrollmentFailed => "Bulut kaydı tamamlanamadı. Lütfen bağlantınızı ve oturumunuzu kontrol edin.",
            BridgeStatus.Disconnected => "Bulut bağlantısı kurulamadı veya kesildi. Yeniden bağlantı bekleniyor.",
            BridgeStatus.AwaitingEnrollment or BridgeStatus.AwaitingApproval => "Bulut bağlantısı için yeniden giriş yapın.",
            _ => null
        });

        if (status == BridgeStatus.Connected)
            _connectionAttempt.TrySetResult(true);
        else if (status is BridgeStatus.Disconnected or BridgeStatus.EnrollmentFailed or BridgeStatus.Stopped)
            _connectionAttempt.TrySetResult(false);
    }

    public void Publish(BridgeStatus status, string? safeDetail)
    {
        if (Changed is not { } handlers) return;

        foreach (Action<BridgeStatus, string?> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(status, safeDetail);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Desktop bridge status subscriber failed.");
            }
        }
    }
}
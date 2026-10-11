namespace Grafirio.Bridge.Desktop.Cloud;

public sealed class DesktopBridgeException(string message, string code = "BRIDGE_UNAVAILABLE", bool retryable = true)
    : Exception(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

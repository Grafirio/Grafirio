namespace Grafirio.Bridge.Desktop.Authentication;

internal sealed class PersistedDesktopSession
{
    public string Issuer { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public UserSession? Session { get; init; }
}
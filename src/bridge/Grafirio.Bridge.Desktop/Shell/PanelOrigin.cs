namespace Grafirio.Bridge.Desktop.Shell;

public sealed class PanelOrigin
{
    public Uri Address { get; }
    public PanelOrigin(string address)
    {
        Address = new Uri(address, UriKind.Absolute);
        if (Address.UserInfo.Length != 0 ||
            (Address.Scheme != Uri.UriSchemeHttps &&
             !(Address.Scheme == Uri.UriSchemeHttp && Address.IsLoopback)))
            throw new ArgumentException("The panel requires HTTPS or loopback HTTP.", nameof(address));
    }

    public bool Contains(string address) => Uri.TryCreate(address, UriKind.Absolute, out var candidate)
        && candidate.UserInfo.Length == 0
        && candidate.Scheme == Address.Scheme
        && candidate.IdnHost == Address.IdnHost
        && candidate.Port == Address.Port;
}
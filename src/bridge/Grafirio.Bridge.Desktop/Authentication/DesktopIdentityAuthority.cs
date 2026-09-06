namespace Grafirio.Bridge.Desktop.Authentication;

public sealed class DesktopIdentityAuthority
{
    public Uri Issuer { get; }
    public string ClientId { get; }

    public DesktopIdentityAuthority(IOptions<BridgeOptions> options)
    {
        var configuration = options.Value;
        if (!Uri.TryCreate(configuration.IdentityUrl?.TrimEnd('/'), UriKind.Absolute, out var issuer)
            || !IsSecure(issuer) || !string.IsNullOrEmpty(issuer.UserInfo)
            || !string.IsNullOrEmpty(issuer.Query) || !string.IsNullOrEmpty(issuer.Fragment))
            throw new InvalidOperationException("Desktop identity requires an HTTPS issuer (HTTP is allowed only for loopback development).");
        if (string.IsNullOrWhiteSpace(configuration.InstallerClientId))
            throw new InvalidOperationException("Desktop identity requires a client identifier.");
        Issuer = issuer;
        ClientId = configuration.InstallerClientId;
    }

    public Uri ValidateEndpoint(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || !IsSecure(endpoint) || endpoint.Scheme != Issuer.Scheme
            || endpoint.IdnHost != Issuer.IdnHost || endpoint.Port != Issuer.Port
            || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new InvalidOperationException("The identity endpoint is outside the trusted authority.");
        return endpoint;
    }

    public bool MatchesIssuer(string? value) =>
        string.Equals(value?.TrimEnd('/'), Issuer.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal);

    private static bool IsSecure(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}
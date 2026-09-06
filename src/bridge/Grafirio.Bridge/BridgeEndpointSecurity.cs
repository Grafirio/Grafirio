using System.Net.Http;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Grafirio.Bridge.Tests")]

namespace Grafirio.Bridge;

/// <summary>Restricts credential-bearing requests to configured HTTPS endpoints.</summary>
public static class BridgeEndpointSecurity
{
    private const string TokenPath = "/protocol/openid-connect/token";
    private const string EnrollmentPath = "/api/bridges/enroll";
    private const string InvalidEndpointMessage = "Bulut bağlantı yapılandırması geçersiz.";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static Uri ValidateBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || !endpoint.IsWellFormedOriginalString()
            || !(endpoint.Scheme == Uri.UriSchemeHttps
                || (endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback))
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new InvalidOperationException(InvalidEndpointMessage);

        return endpoint;
    }

    public static Uri GetEnrollmentEndpoint(BridgeOptions options)
    {
        var server = ValidateBaseUrl(options.ServerUrl);
        ValidateBaseUrl(options.IdentityUrl);
        return new Uri(server.AbsoluteUri.TrimEnd('/') + EnrollmentPath);
    }

    public static Uri ValidateTokenEndpoint(string? value, string? identityUrl)
    {
        var identity = ValidateBaseUrl(identityUrl);
        var endpoint = ValidateBaseUrl(value);
        var expectedPath = identity.AbsolutePath.TrimEnd('/') + TokenPath;

        // Authority alone would allow another realm or an arbitrary receiver on the same host.
        if (!string.Equals(endpoint.Scheme, identity.Scheme, StringComparison.Ordinal)
            || !string.Equals(endpoint.IdnHost, identity.IdnHost, StringComparison.OrdinalIgnoreCase)
            || endpoint.Port != identity.Port
            || !string.Equals(endpoint.AbsolutePath, expectedPath, StringComparison.Ordinal))
            throw new InvalidOperationException(InvalidEndpointMessage);

        return endpoint;
    }

    internal static HttpClientHandler CreateHandler() => new() { AllowAutoRedirect = false };

    internal static HttpClient CreateClient() => new(CreateHandler()) { Timeout = RequestTimeout };
}
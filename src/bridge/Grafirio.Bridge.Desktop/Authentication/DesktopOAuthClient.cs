using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Grafirio.Bridge.Desktop.Authentication;

public sealed class DesktopOAuthClient(
    HttpClient httpClient,
    DesktopIdentityAuthority authority,
    TimeProvider timeProvider)
{
    private const string DiscoveryPath = "/.well-known/openid-configuration";
    private const string InvalidGrant = "invalid_grant";

    public async Task<DesktopIdentityEndpoints> DiscoverAsync(CancellationToken ct)
    {
        var uri = new Uri(authority.Issuer.AbsoluteUri.TrimEnd('/') + DiscoveryPath);
        using var response = await httpClient.GetAsync(uri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var document = await ReadAsync<DesktopDiscoveryDocument>(response, ct).ConfigureAwait(false);
        if (document is null || !authority.MatchesIssuer(document.Issuer))
            throw new InvalidOperationException("The discovery issuer does not match the configured identity.");
        return new DesktopIdentityEndpoints(
            authority.ValidateEndpoint(document.AuthorizationEndpoint),
            authority.ValidateEndpoint(document.TokenEndpoint));
    }

    public async Task<UserSession> ExchangeAsync(
        Uri tokenEndpoint, string code, string verifier, string redirectUri, CancellationToken ct)
    {
        var token = await RequestTokenAsync(tokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = authority.ClientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri
        }, false, ct).ConfigureAwait(false);
        return CreateSession(token!, null);
    }

    public async Task<UserSession?> RefreshAsync(UserSession session, CancellationToken ct)
    {
        var endpoints = await DiscoverAsync(ct).ConfigureAwait(false);
        var token = await RequestTokenAsync(endpoints.TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = authority.ClientId,
            ["refresh_token"] = session.RefreshToken!
        }, true, ct).ConfigureAwait(false);
        return token is null ? null : CreateSession(token, session);
    }

    private async Task<DesktopTokenResponse?> RequestTokenAsync(
        Uri endpoint, Dictionary<string, string> fields, bool refreshing, CancellationToken ct)
    {
        authority.ValidateEndpoint(endpoint.AbsoluteUri);
        using var content = new FormUrlEncodedContent(fields);
        using var response = await httpClient.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (refreshing && response.StatusCode == HttpStatusCode.BadRequest)
            {
                var error = await ReadAsync<DesktopTokenResponse>(response, ct).ConfigureAwait(false);
                if (error?.Error == InvalidGrant) return null;
            }
            // Never include an OAuth response body in exceptions or logs.
            response.EnsureSuccessStatusCode();
        }
        return await ReadAsync<DesktopTokenResponse>(response, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The identity server returned an empty token response.");
    }

    private UserSession CreateSession(DesktopTokenResponse token, UserSession? previous)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn is not > 0
            || !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The identity server returned an invalid token response.");
        DateTimeOffset expiresAt;
        try
        {
            expiresAt = timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("The identity server returned an invalid token lifetime.");
        }
        return new UserSession(token.AccessToken, token.RefreshToken ?? previous?.RefreshToken,
            token.IdToken ?? previous?.IdToken, expiresAt);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The identity server returned malformed JSON.");
        }
    }
}
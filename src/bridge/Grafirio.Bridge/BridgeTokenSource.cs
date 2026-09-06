using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Grafirio.Bridge.Contracts;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge;

/// <summary>
/// Obtains and caches machine tokens only from the configured identity realm.
/// </summary>
public class BridgeTokenSource(
    BridgeState state, ILogger<BridgeTokenSource> logger, IOptions<BridgeOptions> options)
{
    private const string TokenFailureMessage = "Bridge kimliği doğrulanamadı. Lütfen bağlantı ayarlarını kontrol edip yeniden deneyin.";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);
    private readonly Func<HttpClient> _createClient = BridgeEndpointSecurity.CreateClient;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    internal BridgeTokenSource(
        BridgeState state, ILogger<BridgeTokenSource> logger,
        IOptions<BridgeOptions> options, Func<HttpClient> createClient)
        : this(state, logger, options)
    {
        _createClient = createClient;
    }

    public async Task<string> GetAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var configuration = options.Value;
            BridgeEndpointSecurity.GetEnrollmentEndpoint(configuration);
            var credentials = state.Credentials
                ?? throw new InvalidOperationException(TokenFailureMessage);
            var endpoint = BridgeEndpointSecurity.ValidateTokenEndpoint(
                credentials.TokenEndpoint, configuration.IdentityUrl);

            // Revalidate persisted targets even when a cached token is available.
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt - RenewBefore)
                return _token;

            return await FetchAsync(credentials, endpoint, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _token = null;
            _expiresAt = DateTimeOffset.MinValue;
            // Downstream logging must not receive response bodies or exception messages containing secrets.
            logger.LogWarning("Bridge token acquisition failed ({ErrorType}).", exception.GetType().Name);
            throw new InvalidOperationException(TokenFailureMessage);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> FetchAsync(BridgeCredentials credentials, Uri endpoint, CancellationToken ct)
    {
        using var client = _createClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
        });
        using var response = await client.PostAsync(endpoint, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Bridge token request rejected ({StatusCode}).", (int)response.StatusCode);
            throw new InvalidOperationException(TokenFailureMessage);
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (string.IsNullOrWhiteSpace(token?.AccessToken) || token.ExpiresIn <= 0)
            throw new InvalidOperationException(TokenFailureMessage);

        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        _token = token.AccessToken;

        logger.LogDebug("Bridge access token renewed; valid for {Seconds} seconds.", token.ExpiresIn);

        return _token;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }
}

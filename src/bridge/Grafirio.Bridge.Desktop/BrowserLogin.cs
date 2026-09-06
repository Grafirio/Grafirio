using System.Security.Cryptography;
using System.Text;
using System.Web;
using Grafirio.Bridge.Desktop.Authentication;

namespace Grafirio.Bridge.Desktop;

/// <summary>Native authorization code login with PKCE and a loopback callback.</summary>
public sealed class BrowserLogin(
    IOptions<BridgeOptions> options,
    DesktopOAuthClient oauthClient,
    ILogger<BrowserLogin> logger) : IBrowserLogin
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);
    private const int VerifierByteCount = 64;
    private const int StateByteCount = 32;

    public async Task<UserSession?> TryLoginAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(LoginTimeout);
        var endpoints = await oauthClient.DiscoverAsync(deadline.Token);
        using var listener = new LoopbackListener();
        var verifier = RandomUrlSafe(VerifierByteCount);
        var state = RandomUrlSafe(StateByteCount);
        var query = HttpUtility.ParseQueryString(endpoints.AuthorizationEndpoint.Query);
        query["client_id"] = options.Value.InstallerClientId;
        query["response_type"] = "code";
        query["redirect_uri"] = listener.RedirectUri;
        query["scope"] = "openid profile email";
        query["state"] = state;
        query["code_challenge"] = UrlSafe(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        query["code_challenge_method"] = "S256";
        var authorizeUri = new UriBuilder(endpoints.AuthorizationEndpoint) { Query = query.ToString() };

        deadline.Token.ThrowIfCancellationRequested();
        logger.LogInformation("Waiting for browser sign-in.");
        Browser.Open(authorizeUri.Uri.AbsoluteUri);
        var callback = await listener.WaitForCallbackAsync(state, LoginTimeout, deadline.Token);
        if (callback.Error is not null)
        {
            logger.LogInformation("Browser sign-in was not authorized.");
            return null;
        }

        var session = await oauthClient.ExchangeAsync(
            endpoints.TokenEndpoint, callback.Code!, verifier, listener.RedirectUri, deadline.Token);
        logger.LogInformation("Browser sign-in completed.");
        return session;
    }

    private static string RandomUrlSafe(int bytes) => UrlSafe(RandomNumberGenerator.GetBytes(bytes));
    private static string UrlSafe(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

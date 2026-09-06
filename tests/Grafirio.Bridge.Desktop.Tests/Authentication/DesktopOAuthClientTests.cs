using System.Net;
using System.Net.Http;
using Grafirio.Bridge.Desktop.Authentication;
using Xunit;

namespace Grafirio.Bridge.Desktop.Tests.Authentication;

public sealed class DesktopOAuthClientTests
{
    [Theory]
    [InlineData("https://attacker.example/token")]
    [InlineData("http://identity.example/token")]
    [InlineData("https://identity.example:8443/token")]
    [InlineData("https://user:password@identity.example/token")]
    [InlineData("https://identity.example/token#fragment")]
    [InlineData("/relative/token")]
    public void RejectsUntrustedEndpoints(string endpoint) =>
        Assert.Throws<InvalidOperationException>(() => AuthenticationTestContext.Authority().ValidateEndpoint(endpoint));

    [Theory]
    [InlineData("http://identity.example/realm")]
    [InlineData("ftp://localhost/realm")]
    [InlineData("https://identity.example/realm?query=true")]
    public void RejectsInsecureIssuer(string issuer) =>
        Assert.Throws<InvalidOperationException>(() => AuthenticationTestContext.Authority(issuer));

    [Theory]
    [InlineData("http://localhost:8080/realms/grafirio")]
    [InlineData("http://127.0.0.1:8080/realms/grafirio")]
    [InlineData("http://[::1]:8080/realms/grafirio")]
    public void AllowsLoopbackDevelopmentIssuer(string issuer)
    {
        var authority = AuthenticationTestContext.Authority(issuer);
        Assert.Equal(new Uri(issuer + "/token"), authority.ValidateEndpoint(issuer + "/token"));
    }

    [Theory]
    [InlineData("https://other.example/realm", "https://identity.example/token")]
    [InlineData(AuthenticationTestContext.Issuer, "https://attacker.example/token")]
    public async Task DiscoveryRejectsIssuerOrTokenAuthorityMismatch(string issuer, string tokenEndpoint)
    {
        using var handler = new DiscoveryHandler($$"""
            {"issuer":"{{issuer}}","authorization_endpoint":"https://identity.example/authorize",
             "token_endpoint":"{{tokenEndpoint}}"}
            """);
        using var http = new HttpClient(handler);
        var client = new DesktopOAuthClient(http, AuthenticationTestContext.Authority(), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DiscoverAsync(CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ExchangePostsPkceAndSetsNativeExpiry()
    {
        using var context = new AuthenticationTestContext();
        var session = await context.OAuth.ExchangeAsync(new Uri("https://identity.example/token"),
            "authorization-code", "pkce-verifier", "http://127.0.0.1:5000/callback", CancellationToken.None);
        var body = Assert.Single(context.Handler.TokenRequests);
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code_verifier=pkce-verifier", body);
        Assert.DoesNotContain("client_secret", body);
        Assert.Equal(context.Clock.Now.AddMinutes(5), session.ExpiresAt);
    }

    [Fact]
    public async Task MalformedTokenResponseDoesNotExposeResponseContent()
    {
        using var context = new AuthenticationTestContext();
        context.Store.Session = context.Session();
        context.Handler.Respond = (_, _) => Task.FromResult(AuthenticationTestContext.Json(
            """{"access_token":"private-marker","expires_in":"private-marker"}"""));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Manager.RefreshAsync(CancellationToken.None));
        Assert.DoesNotContain("private-marker", exception.ToString());
        Assert.Equal(0, context.Store.Deletes);
    }

    private sealed class DiscoveryHandler(string document) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            return Task.FromResult(AuthenticationTestContext.Json(document, HttpStatusCode.OK));
        }
    }
}
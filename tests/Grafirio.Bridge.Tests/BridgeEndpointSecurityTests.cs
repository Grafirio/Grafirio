namespace Grafirio.Bridge.Tests;

public sealed class BridgeEndpointSecurityTests
{
    private const string IdentityUrl = "https://identity.example/auth/realms/grafirio";
    private const string TokenPath = "/protocol/openid-connect/token";

    [Theory]
    [InlineData("https://cloud.example")]
    [InlineData("https://cloud.example:8443/base/")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void SecureOrLoopbackBaseUrlsAreAccepted(string value)
    {
        Assert.Equal(new Uri(value), BridgeEndpointSecurity.ValidateBaseUrl(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative")]
    [InlineData("//identity.example/path")]
    [InlineData("http://cloud.example")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://localhost.attacker.example")]
    [InlineData("file:///secret")]
    [InlineData("ftp://localhost")]
    [InlineData("https://user:secret@cloud.example")]
    [InlineData("https://cloud.example?secret=value")]
    [InlineData("https://cloud.example#secret")]
    [InlineData("https://cloud.example\\path")]
    public void UnsafeBaseUrlsAreRejectedWithoutEchoingInput(string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BridgeEndpointSecurity.ValidateBaseUrl(value));
        Assert.DoesNotContain("secret", exception.ToString());
    }

    [Theory]
    [InlineData(IdentityUrl, IdentityUrl + TokenPath)]
    [InlineData(IdentityUrl + "/", IdentityUrl + TokenPath)]
    [InlineData(IdentityUrl, "https://IDENTITY.example:443/auth/realms/grafirio" + TokenPath)]
    [InlineData("http://localhost:8080/realms/grafirio", "http://localhost:8080/realms/grafirio" + TokenPath)]
    [InlineData("http://127.0.0.1:8080/realms/grafirio", "http://127.0.0.1:8080/realms/grafirio" + TokenPath)]
    [InlineData("http://[::1]:8080/realms/grafirio", "http://[::1]:8080/realms/grafirio" + TokenPath)]
    public void ExactRealmTokenEndpointIsAccepted(string identity, string token)
    {
        Assert.Equal(new Uri(token), BridgeEndpointSecurity.ValidateTokenEndpoint(token, identity));
    }

    [Theory]
    [InlineData("https://attacker.example/auth/realms/grafirio" + TokenPath)]
    [InlineData("https://identity.example.attacker.example/auth/realms/grafirio" + TokenPath)]
    [InlineData("https://identity.example:8443/auth/realms/grafirio" + TokenPath)]
    [InlineData("http://identity.example/auth/realms/grafirio" + TokenPath)]
    [InlineData("https://identity.example/auth/realms/other" + TokenPath)]
    [InlineData("https://identity.example/auth/realms/grafirio-other" + TokenPath)]
    [InlineData(IdentityUrl + "/receiver")]
    [InlineData(IdentityUrl + TokenPath + "/extra")]
    [InlineData(IdentityUrl + TokenPath + "?secret=value")]
    [InlineData(IdentityUrl + TokenPath + "#secret")]
    [InlineData("https://secret@identity.example/auth/realms/grafirio" + TokenPath)]
    [InlineData(IdentityUrl + "/../other" + TokenPath)]
    [InlineData(IdentityUrl + "/%2e%2e/other" + TokenPath)]
    [InlineData(IdentityUrl + "/protocol%2fopenid-connect/token")]
    [InlineData("/protocol/openid-connect/token")]
    public void UntrustedTokenTargetsAreRejected(string token)
    {
        Assert.Throws<InvalidOperationException>(() =>
            BridgeEndpointSecurity.ValidateTokenEndpoint(token, IdentityUrl));
    }

    [Theory]
    [InlineData("http://localhost:8080/realms/grafirio", "https://localhost:8080/realms/grafirio" + TokenPath)]
    [InlineData("http://localhost:8080/realms/grafirio", "http://127.0.0.1:8080/realms/grafirio" + TokenPath)]
    [InlineData("http://localhost:8080/realms/grafirio", "http://localhost:8081/realms/grafirio" + TokenPath)]
    public void LoopbackDoesNotBypassAuthorityEquality(string identity, string token)
    {
        Assert.Throws<InvalidOperationException>(() =>
            BridgeEndpointSecurity.ValidateTokenEndpoint(token, identity));
    }

    [Fact]
    public void EnrollmentRetainsConfiguredBasePath()
    {
        var endpoint = BridgeEndpointSecurity.GetEnrollmentEndpoint(new BridgeOptions
        {
            ServerUrl = "https://cloud.example/gateway/",
            IdentityUrl = IdentityUrl
        });
        Assert.Equal("https://cloud.example/gateway/api/bridges/enroll", endpoint.AbsoluteUri);
    }

    [Fact]
    public void CredentialTransportDisablesRedirects()
    {
        using var handler = BridgeEndpointSecurity.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
    }
}
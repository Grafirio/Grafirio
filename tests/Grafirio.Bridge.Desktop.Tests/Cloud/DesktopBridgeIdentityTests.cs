using System.IO;
using System.Text;
using System.Text.Json;
using Grafirio.Bridge.Desktop.Cloud;
using Xunit;

namespace Grafirio.Bridge.Desktop.Tests.Cloud;

public sealed class DesktopBridgeIdentityTests
{
    private const string Issuer = "https://identity.example/realms/desktop";
    private const string Subject = "user-one";
    private const string Company = "company-one";

    [Fact]
    public void RefreshedTokensUseSameAbsoluteIdentityDirectory()
    {
        var first = DesktopBridgeIdentity.GetDirectory(Session(Issuer, Subject, Company));
        var refreshed = DesktopBridgeIdentity.GetDirectory(Session(Issuer, Subject, Company, "refreshed"));

        Assert.Equal(first, refreshed);
        Assert.True(Path.IsPathFullyQualified(first));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Grafirio", "Desktop", "Cloud"), Path.GetDirectoryName(first));
        Assert.Matches("^[a-f0-9]{64}$", Path.GetFileName(first));
        Assert.DoesNotContain(Subject, first);
        Assert.DoesNotContain(Company, first);
    }

    [Theory]
    [InlineData("https://other.example/realms/desktop", Subject, Company)]
    [InlineData(Issuer, "user-two", Company)]
    [InlineData(Issuer, Subject, "company-two")]
    public void EveryScopeComponentPartitionsStorage(string issuer, string subject, string company)
    {
        Assert.NotEqual(DesktopBridgeIdentity.GetDirectory(Session(Issuer, Subject, Company)),
            DesktopBridgeIdentity.GetDirectory(Session(issuer, subject, company)));
    }

    [Theory]
    [InlineData(null, Subject, Company)]
    [InlineData(Issuer, null, Company)]
    [InlineData(Issuer, Subject, null)]
    [InlineData(Issuer, Subject, " ")]
    public void MissingIdentityFailsWithLocalizedMessage(string? issuer, string? subject, string? company)
    {
        var failure = Assert.Throws<DesktopBridgeException>(() =>
            DesktopBridgeIdentity.GetDirectory(Session(issuer, subject, company)));
        Assert.Contains("şirket", failure.Message);
    }

    [Fact]
    public void ExpiredSessionAndTokenAreRejected()
    {
        var session = Session(Issuer, Subject, Company);
        Assert.Throws<DesktopBridgeException>(() => DesktopBridgeIdentity.GetDirectory(
            session with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }));
        Assert.Throws<DesktopBridgeException>(() => DesktopBridgeIdentity.GetDirectory(
            Session(Issuer, Subject, Company, expired: true)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("header.!.signature")]
    [InlineData("header.bnVsbA.signature")]
    public void MalformedTokensFailWithoutEchoingInput(string token)
    {
        var failure = Assert.Throws<DesktopBridgeException>(() =>
            DesktopBridgeIdentity.GetDirectory(new UserSession(token, null, null)));
        Assert.Contains("yeniden giriş", failure.Message);
    }

    private static UserSession Session(string? issuer, string? subject, string? company,
        string nonce = "initial", bool expired = false)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer,
            sub = subject,
            company_id = company,
            nonce,
            exp = DateTimeOffset.UtcNow.AddMinutes(expired ? -5 : 5).ToUnixTimeSeconds()
        });
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\"}"));
        var encoded = Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new UserSession($"{header}.{encoded}.test-signature", null, null);
    }
}
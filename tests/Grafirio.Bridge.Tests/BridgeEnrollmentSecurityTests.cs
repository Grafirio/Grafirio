using System.Net;
using System.Net.Http;

namespace Grafirio.Bridge.Tests;

public sealed class BridgeEnrollmentSecurityTests
{
    [Theory]
    [InlineData("http://cloud.example", BridgeSecurityTestContext.IdentityUrl)]
    [InlineData("https://cloud.example", "http://identity.example/realms/grafirio")]
    [InlineData("https://secret@cloud.example", BridgeSecurityTestContext.IdentityUrl)]
    public async Task InvalidConfigurationIsRejectedBeforeClientCreation(string server, string identity)
    {
        using var context = new BridgeSecurityTestContext();
        context.Options.ServerUrl = server;
        context.Options.IdentityUrl = identity;
        var enrollment = context.CreateEnrollment();

        Assert.False(await enrollment.EnrollWithTokenAsync(BridgeSecurityTestContext.InstallerToken, CancellationToken.None));
        Assert.False(await enrollment.TryEnrollAsync(CancellationToken.None));
        Assert.Equal(0, context.ClientCreations);
        Assert.False(File.Exists(context.State.FilePath));
        context.AssertNoSecretsExposed();
    }

    [Theory]
    [InlineData("https://attacker.example/token")]
    [InlineData("https://identity.example/realms/other/protocol/openid-connect/token")]
    [InlineData("http://identity.example/realms/grafirio/protocol/openid-connect/token")]
    [InlineData("https://identity.example/realms/grafirio/receiver")]
    [InlineData(BridgeSecurityTestContext.TokenEndpoint + "?secret=value")]
    public async Task InvalidResponseTargetIsRejectedBeforeSaving(string endpoint)
    {
        using var context = new BridgeSecurityTestContext();
        context.SetEnrollmentResponse(endpoint);

        Assert.False(await context.CreateEnrollment().EnrollWithTokenAsync(
            BridgeSecurityTestContext.InstallerToken, CancellationToken.None));
        Assert.Equal(1, context.RequestCount);
        Assert.False(context.State.IsEnrolled);
        Assert.False(File.Exists(context.State.FilePath));
        context.AssertNoSecretsExposed();
    }

    [Fact]
    public async Task RejectedResponseLeavesExistingEnrollmentUnchanged()
    {
        using var context = new BridgeSecurityTestContext();
        context.StoreCredentials();
        var before = File.ReadAllBytes(context.State.FilePath);
        var bridgeId = context.State.BridgeId;
        context.SetEnrollmentResponse("https://attacker.example/token");

        Assert.False(await context.CreateEnrollment().EnrollWithTokenAsync(
            BridgeSecurityTestContext.InstallerToken, CancellationToken.None));
        Assert.Equal(bridgeId, context.State.BridgeId);
        Assert.Equal(before, File.ReadAllBytes(context.State.FilePath));
    }

    [Theory]
    [InlineData("https://cloud.example", BridgeSecurityTestContext.IdentityUrl)]
    [InlineData("http://127.0.0.1:5221", "http://localhost:8080/realms/grafirio")]
    public async Task ValidEnrollmentPersistsCredentials(string server, string identity)
    {
        using var context = new BridgeSecurityTestContext();
        context.Options.ServerUrl = server;
        context.Options.IdentityUrl = identity;
        var tokenEndpoint = identity + "/protocol/openid-connect/token";
        context.SetEnrollmentResponse(tokenEndpoint);

        Assert.True(await context.CreateEnrollment().EnrollWithTokenAsync(
            BridgeSecurityTestContext.InstallerToken, CancellationToken.None));
        Assert.Equal(server + "/api/bridges/enroll", context.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer " + BridgeSecurityTestContext.InstallerToken, context.Authorization);
        Assert.True(context.State.IsEnrolled);
        context.State.Load();
        Assert.Equal(tokenEndpoint, context.State.Credentials!.TokenEndpoint);
        context.AssertNoSecretsExposed();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task FailureAndRedirectResponsesDoNotExposeBodyOrSave(HttpStatusCode status)
    {
        using var context = new BridgeSecurityTestContext();
        context.StatusCode = status;
        context.Location = "https://attacker.example/receiver";
        context.ResponseBody = BridgeSecurityTestContext.Secret + BridgeSecurityTestContext.InstallerToken;

        Assert.False(await context.CreateEnrollment().EnrollWithTokenAsync(
            BridgeSecurityTestContext.InstallerToken, CancellationToken.None));
        Assert.Equal(1, context.RequestCount);
        Assert.False(File.Exists(context.State.FilePath));
        context.AssertNoSecretsExposed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedResponseAndTransportErrorsAreSanitized(bool transportFailure)
    {
        using var context = new BridgeSecurityTestContext();
        context.ResponseBody = "{\"bridgeId\":\"" + BridgeSecurityTestContext.Secret + "\"}";
        if (transportFailure) context.RequestFailure = new HttpRequestException(BridgeSecurityTestContext.Secret);

        Assert.False(await context.CreateEnrollment().EnrollWithTokenAsync(
            BridgeSecurityTestContext.InstallerToken, CancellationToken.None));
        Assert.False(File.Exists(context.State.FilePath));
        context.AssertNoSecretsExposed();
    }
}
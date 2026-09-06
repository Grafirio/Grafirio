using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge.Tests;

public sealed class BridgeTokenSourceSecurityTests
{
    [Theory]
    [InlineData("https://attacker.example/token")]
    [InlineData("http://identity.example/realms/grafirio/protocol/openid-connect/token")]
    [InlineData("https://identity.example/realms/other/protocol/openid-connect/token")]
    [InlineData(BridgeSecurityTestContext.TokenEndpoint + "?secret=value")]
    public async Task InvalidStoredTargetIsRejectedBeforeClientCreation(string endpoint)
    {
        using var context = new BridgeSecurityTestContext();
        context.StoreCredentials(endpoint);
        context.State.Load();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.CreateTokenSource().GetAsync());
        Assert.Equal(0, context.ClientCreations);
        Assert.DoesNotContain(endpoint, failure.ToString());
        Assert.Null(failure.InnerException);
        context.AssertNoSecretsExposed();
    }

    [Fact]
    public async Task InvalidServerIsRejectedBeforeSendingMachineCredentials()
    {
        using var context = new BridgeSecurityTestContext();
        context.StoreCredentials();
        context.Options.ServerUrl = "http://cloud.example";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.CreateTokenSource().GetAsync());
        Assert.Equal(0, context.ClientCreations);
    }

    [Theory]
    [InlineData(BridgeSecurityTestContext.IdentityUrl)]
    [InlineData("http://localhost:8080/realms/grafirio")]
    public async Task ValidTargetReceivesCredentialsAndTokenIsCached(string identity)
    {
        using var context = new BridgeSecurityTestContext();
        context.Options.IdentityUrl = identity;
        var endpoint = identity + "/protocol/openid-connect/token";
        context.StoreCredentials(endpoint);
        context.ResponseBody = "{\"access_token\":\"" + BridgeSecurityTestContext.AccessToken + "\",\"expires_in\":300}";
        var source = context.CreateTokenSource();

        Assert.Equal(BridgeSecurityTestContext.AccessToken, await source.GetAsync());
        Assert.Equal(BridgeSecurityTestContext.AccessToken, await source.GetAsync());
        Assert.Equal(1, context.RequestCount);
        Assert.Equal(endpoint, context.RequestUri!.AbsoluteUri);
        Assert.Contains("client_secret=" + BridgeSecurityTestContext.Secret, context.RequestBody);
        Assert.Contains("grant_type=client_credentials", context.RequestBody);
        context.AssertNoSecretsExposed();
    }

    [Fact]
    public async Task StoredTargetIsRevalidatedBeforeReturningCachedToken()
    {
        using var context = new BridgeSecurityTestContext();
        context.StoreCredentials();
        context.ResponseBody = "{\"access_token\":\"cached\",\"expires_in\":300}";
        var source = context.CreateTokenSource();
        Assert.Equal("cached", await source.GetAsync());
        context.StoreCredentials("https://attacker.example/token");

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetAsync());
        Assert.Equal(1, context.ClientCreations);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task FailedAndRedirectResponsesNeverExposeSecrets(HttpStatusCode status)
    {
        using var context = new BridgeSecurityTestContext();
        context.StoreCredentials();
        context.StatusCode = status;
        context.Location = "https://attacker.example/receiver";
        context.ResponseBody = BridgeSecurityTestContext.Secret + BridgeSecurityTestContext.AccessToken;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.CreateTokenSource().GetAsync());
        Assert.Equal(1, context.RequestCount);
        Assert.DoesNotContain(BridgeSecurityTestContext.Secret, failure.ToString());
        Assert.DoesNotContain(BridgeSecurityTestContext.AccessToken, failure.ToString());
        Assert.Null(failure.InnerException);
        context.AssertNoSecretsExposed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedResponseAndTransportErrorsAreSanitized(bool transportFailure)
    {
        using var context = new BridgeSecurityTestContext();
        context.StoreCredentials();
        context.ResponseBody = "{\"expires_in\":\"" + BridgeSecurityTestContext.Secret + "\"}";
        if (transportFailure) context.RequestFailure = new HttpRequestException(BridgeSecurityTestContext.Secret);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.CreateTokenSource().GetAsync());
        Assert.DoesNotContain(BridgeSecurityTestContext.Secret, failure.ToString());
        Assert.Null(failure.InnerException);
        context.AssertNoSecretsExposed();
    }

    [Fact]
    public async Task CallerCancellationIsNotWrapped()
    {
        using var context = new BridgeSecurityTestContext();
        context.StoreCredentials();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.CreateTokenSource().GetAsync(cancellation.Token));
        Assert.Equal(0, context.ClientCreations);
    }

    [Fact]
    public void CoreRegistrationResolvesRequiredOptionsWithoutChangingRegistrations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<BridgeOptions>>(Options.Create(new BridgeOptions()));
        services.AddSingleton<IBridgeDisplay>(new BridgeSecurityTestContext.RecordingDisplay());
        services.AddBridgeCore(runInBackground: false);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.NotNull(provider.GetRequiredService<BridgeTokenSource>());
        Assert.NotNull(provider.GetRequiredService<BridgeEnrollment>());
    }
}
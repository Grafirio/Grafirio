using System.Net;
using System.Net.Http;
using Xunit;

namespace Grafirio.Bridge.Desktop.Tests.Authentication;

public sealed class DesktopSessionManagerTests
{
    [Fact]
    public async Task RestoreAndGetReuseFreshSessionButExplicitRefreshAlwaysRequestsTokens()
    {
        using var context = new AuthenticationTestContext();
        context.Store.Session = context.Session();
        var changes = 0;
        context.Manager.SessionChanged += () => changes++;

        await context.Manager.RestoreAsync(CancellationToken.None);
        Assert.Equal(context.Store.Session, await context.Manager.GetAsync(CancellationToken.None));
        Assert.Empty(context.Handler.TokenRequests);
        var refreshed = await context.Manager.RefreshAsync(CancellationToken.None);

        Assert.Single(context.Handler.TokenRequests);
        Assert.Equal("new-access", refreshed!.AccessToken);
        Assert.Equal(context.Clock.Now.AddSeconds(300), refreshed.ExpiresAt);
        Assert.Equal(refreshed, context.Store.Session);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task GetRefreshesNearExpiryAndKeepsTokensOmittedByServer()
    {
        using var context = new AuthenticationTestContext();
        context.Store.Session = context.Session(TimeSpan.FromSeconds(60));
        context.Handler.Respond = (_, _) => Task.FromResult(AuthenticationTestContext.Json(
            """{"access_token":"rotated","token_type":"Bearer","expires_in":600}"""));

        var session = await context.Manager.GetAsync(CancellationToken.None);

        Assert.Equal("rotated", session!.AccessToken);
        Assert.Equal("old-refresh", session.RefreshToken);
        Assert.Equal("old-id", session.IdToken);
        Assert.Equal(context.Clock.Now.AddMinutes(10), session.ExpiresAt);
    }

    [Fact]
    public async Task InvalidGrantClearsSessionAndRaisesChange()
    {
        using var context = new AuthenticationTestContext();
        context.Store.Session = context.Session();
        await context.Manager.RestoreAsync(CancellationToken.None);
        var changes = 0;
        context.Manager.SessionChanged += () => changes++;
        context.Handler.Respond = (_, _) => Task.FromResult(AuthenticationTestContext.Json(
            """{"error":"invalid_grant"}""", HttpStatusCode.BadRequest));

        Assert.Null(await context.Manager.RefreshAsync(CancellationToken.None));
        Assert.Null(context.Manager.Current);
        Assert.Null(context.Store.Session);
        Assert.Equal(1, context.Store.Deletes);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfflineOrTimeoutPreservesPersistedAndCurrentSession(bool timeout)
    {
        using var context = new AuthenticationTestContext();
        var original = context.Store.Session = context.Session();
        await context.Manager.RestoreAsync(CancellationToken.None);
        var changes = 0;
        context.Manager.SessionChanged += () => changes++;
        context.Handler.Respond = (_, _) => throw (timeout
            ? new TaskCanceledException("Simulated timeout.")
            : new HttpRequestException("Simulated offline state."));

        if (timeout)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Manager.RefreshAsync(CancellationToken.None));
        else
            await Assert.ThrowsAsync<HttpRequestException>(() => context.Manager.RefreshAsync(CancellationToken.None));

        Assert.Equal(original, context.Manager.Current);
        Assert.Equal(original, context.Store.Session);
        Assert.Equal(0, context.Store.Deletes);
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task NonInvalidGrantFailuresDoNotClearSession(HttpStatusCode status)
    {
        using var context = new AuthenticationTestContext();
        var original = context.Store.Session = context.Session();
        context.Handler.Respond = (_, _) => Task.FromResult(AuthenticationTestContext.Json(
            """{"error":"invalid_client","error_description":"private-response-marker"}""", status));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => context.Manager.RefreshAsync(CancellationToken.None));

        Assert.DoesNotContain("private-response-marker", exception.ToString());
        Assert.Equal(original, context.Store.Session);
        Assert.Equal(original, context.Manager.Current);
        Assert.Equal(0, context.Store.Deletes);
    }

    [Fact]
    public async Task RestoreOfflineKeepsExpiredSessionForSubsequentRetry()
    {
        using var context = new AuthenticationTestContext();
        var original = context.Store.Session = context.Session(TimeSpan.FromMinutes(-1));
        context.Handler.Respond = (_, _) => throw new HttpRequestException("Offline.");
        await Assert.ThrowsAsync<HttpRequestException>(() => context.Manager.RestoreAsync(CancellationToken.None));
        Assert.Equal(original, context.Manager.Current);
        Assert.Equal(original, context.Store.Session);

        context.Handler.Respond = (_, _) => Task.FromResult(AuthenticationTestContext.Json(AuthenticationTestContext.TokenResponse));
        Assert.Equal("new-access", (await context.Manager.GetAsync(CancellationToken.None))!.AccessToken);
    }

    [Fact]
    public async Task ConcurrentRefreshesUseRotatedTokenAndSignOutCannotBeUndone()
    {
        using var context = new AuthenticationTestContext();
        context.Store.Session = context.Session();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Handler.Respond = async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return AuthenticationTestContext.Json(AuthenticationTestContext.TokenResponse);
        };

        var first = context.Manager.RefreshAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = context.Manager.RefreshAsync(CancellationToken.None);
        var signOut = context.Manager.SignOutAsync(CancellationToken.None);
        Assert.Single(context.Handler.TokenRequests);
        release.SetResult();
        await Task.WhenAll(first, second, signOut);

        Assert.Equal(2, context.Handler.TokenRequests.Count);
        Assert.Contains("refresh_token=old-refresh", context.Handler.TokenRequests[0]);
        Assert.Contains("refresh_token=new-refresh", context.Handler.TokenRequests[1]);
        Assert.Null(context.Manager.Current);
        Assert.Null(context.Store.Session);
    }

    [Fact]
    public async Task CancellationDuringRefreshAndWhileQueuedDoesNotClearSession()
    {
        using var context = new AuthenticationTestContext();
        var original = context.Store.Session = context.Session();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var activeCancellation = new CancellationTokenSource();
        context.Handler.Respond = async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable.");
        };

        var active = context.Manager.RefreshAsync(activeCancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var queuedCancellation = new CancellationTokenSource();
        var queued = context.Manager.SignOutAsync(queuedCancellation.Token);
        queuedCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        activeCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        Assert.Equal(original, context.Manager.Current);
        Assert.Equal(original, context.Store.Session);
    }

    [Fact]
    public async Task SignInPersistsAndSignOutRemovesSessionWithReentrantEventReads()
    {
        using var context = new AuthenticationTestContext();
        context.Browser.Session = context.Session();
        var observed = new List<UserSession?>();
        context.Manager.SessionChanged += () => observed.Add(context.Manager.Current);
        var signedIn = await context.Manager.SignInAsync(CancellationToken.None);
        Assert.Equal(signedIn, context.Store.Session);
        await context.Manager.SignOutAsync(CancellationToken.None);
        Assert.Equal(new UserSession?[] { signedIn, null }, observed);
        Assert.Null(await context.Manager.RefreshAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DeclinedSignInPreservesExistingSession()
    {
        using var context = new AuthenticationTestContext();
        var original = context.Store.Session = context.Session();
        await context.Manager.RestoreAsync(CancellationToken.None);
        Assert.Null(await context.Manager.SignInAsync(CancellationToken.None));
        Assert.Equal(original, context.Manager.Current);
        Assert.Equal(original, context.Store.Session);
    }

    [Fact]
    public async Task ExpiredSessionWithoutRefreshTokenIsCleared()
    {
        using var context = new AuthenticationTestContext();
        context.Store.Session = context.Session(TimeSpan.FromMinutes(-1)) with { RefreshToken = null };
        Assert.Null(await context.Manager.GetAsync(CancellationToken.None));
        Assert.Null(context.Store.Session);
        Assert.Empty(context.Handler.TokenRequests);
    }

    [Fact]
    public async Task ValidSessionWithoutRefreshTokenRemainsUsableUntilExpiry()
    {
        using var context = new AuthenticationTestContext();
        var original = context.Store.Session = context.Session(TimeSpan.FromSeconds(30)) with { RefreshToken = null };
        Assert.Equal(original, await context.Manager.GetAsync(CancellationToken.None));
        Assert.Equal(original, context.Store.Session);
        Assert.Empty(context.Handler.TokenRequests);
    }

    [Fact]
    public void SessionStringDoesNotExposeTokens()
    {
        var text = new UserSession("access-secret", "refresh-secret", "id-secret").ToString();
        Assert.DoesNotContain("access-secret", text);
        Assert.DoesNotContain("refresh-secret", text);
        Assert.DoesNotContain("id-secret", text);
    }
}
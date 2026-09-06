using System.Net;
using System.Net.Http;
using Xunit;

namespace Grafirio.Bridge.Desktop.Tests.Authentication;

public sealed class LoopbackListenerTests
{
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(10);
    private const string State = "expected-state";

    [Fact]
    public async Task RejectsWrongPathStateAndAmbiguousPayloadBeforeAcceptingCode()
    {
        using var listener = new LoopbackListener();
        using var client = new HttpClient { Timeout = CallbackTimeout };
        var callback = listener.WaitForCallbackAsync(State, CallbackTimeout, CancellationToken.None);
        var root = new Uri(new Uri(listener.RedirectUri), "/");
        using var wrongPath = await client.GetAsync(new Uri(root, "favicon.ico"));
        Assert.Equal(HttpStatusCode.NotFound, wrongPath.StatusCode);
        using var wrongState = await client.GetAsync(listener.RedirectUri + "?state=wrong&code=secret-code");
        Assert.Equal(HttpStatusCode.BadRequest, wrongState.StatusCode);
        Assert.DoesNotContain("secret-code", await wrongState.Content.ReadAsStringAsync());
        using var ambiguous = await client.GetAsync(listener.RedirectUri + $"?state={State}&code=code&error=access_denied");
        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        using var duplicate = await client.GetAsync(listener.RedirectUri + $"?state={State}&state={State}&code=code");
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.False(callback.IsCompleted);

        using var valid = await client.GetAsync(listener.RedirectUri + $"?state={State}&code=valid-code");
        var page = await valid.Content.ReadAsStringAsync();
        Assert.Contains("Giriş yanıtı alındı", page);
        Assert.DoesNotContain("Giriş tamamlandı", page);
        Assert.DoesNotContain("valid-code", page);
        Assert.Equal("valid-code", (await callback).Code);
    }

    [Fact]
    public async Task OAuthErrorIsValidatedAndNeverShownAsSuccessOrReflectedIntoHtml()
    {
        using var listener = new LoopbackListener();
        using var client = new HttpClient { Timeout = CallbackTimeout };
        var callback = listener.WaitForCallbackAsync(State, CallbackTimeout, CancellationToken.None);
        using var response = await client.GetAsync(listener.RedirectUri +
            $"?state={State}&error=access_denied&error_description=private-description");
        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("Giriş tamamlanmadı", page);
        Assert.DoesNotContain("private-description", page);
        Assert.Equal("access_denied", (await callback).Error);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var listener = new LoopbackListener();
        using var cancellation = new CancellationTokenSource();
        var pending = listener.WaitForCallbackAsync(State, CallbackTimeout, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task DeadlinePropagatesInsteadOfReturningNull()
    {
        using var listener = new LoopbackListener();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            listener.WaitForCallbackAsync(State, TimeSpan.Zero, CancellationToken.None));
    }
}
using System.IO;
using System.Text;
using Grafirio.Bridge.Desktop.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Grafirio.Bridge.Desktop.Tests.Authentication;

public sealed class DpapiDesktopSessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "authentication-artifacts", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FirstRunSignOutDoesNotRequireStorageDirectory()
    {
        await CreateStore().DeleteAsync(CancellationToken.None);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task RoundTripEncryptsTokensAndDeletesSession()
    {
        var store = CreateStore();
        var session = new UserSession("private-access", "private-refresh", "private-id", DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.Null(await store.LoadAsync(CancellationToken.None));
        await store.SaveAsync(session, CancellationToken.None);
        Assert.Equal(session, await CreateStore().LoadAsync(CancellationToken.None));
        var file = Assert.Single(Directory.GetFiles(_directory));
        var contents = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
        Assert.DoesNotContain(session.AccessToken, contents);
        Assert.DoesNotContain(session.RefreshToken!, contents);
        Assert.DoesNotContain(session.IdToken!, contents);
        await store.DeleteAsync(CancellationToken.None);
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("https://identity.example/realms/other", AuthenticationTestContext.ClientId)]
    [InlineData(AuthenticationTestContext.Issuer, "other-client")]
    public async Task DifferentIssuerOrClientCannotReadSession(string issuer, string clientId)
    {
        var store = CreateStore();
        var session = new UserSession("access", "refresh", "id");
        await store.SaveAsync(session, CancellationToken.None);
        var other = new DpapiDesktopSessionStore(AuthenticationTestContext.Authority(issuer, clientId),
            NullLogger<DpapiDesktopSessionStore>.Instance, _directory);
        Assert.Null(await other.LoadAsync(CancellationToken.None));
        Assert.Equal(session, await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CorruptFileIsNotTreatedAsSession()
    {
        var store = CreateStore();
        await store.SaveAsync(new UserSession("access", "refresh", null), CancellationToken.None);
        await File.WriteAllTextAsync(Assert.Single(Directory.GetFiles(_directory)), "corrupt");
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancelledSaveAndDeletePreserveExistingSession()
    {
        var store = CreateStore();
        var session = new UserSession("access", "refresh", null);
        await store.SaveAsync(session, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(new UserSession("replacement", null, null), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DeleteAsync(cancellation.Token));
        Assert.Equal(session, await store.LoadAsync(CancellationToken.None));
    }

    private DpapiDesktopSessionStore CreateStore() => new(AuthenticationTestContext.Authority(),
        NullLogger<DpapiDesktopSessionStore>.Instance, _directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
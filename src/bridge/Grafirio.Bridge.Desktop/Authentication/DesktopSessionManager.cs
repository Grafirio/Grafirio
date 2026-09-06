namespace Grafirio.Bridge.Desktop.Authentication;

public sealed class DesktopSessionManager(
    IBrowserLogin browserLogin,
    DesktopOAuthClient oauthClient,
    IDesktopSessionStore store,
    TimeProvider timeProvider) : IDesktopSessionManager, IDisposable
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private UserSession? _current;
    private bool _restored;

    public UserSession? Current => Volatile.Read(ref _current);
    public event Action? SessionChanged;

    public async Task RestoreAsync(CancellationToken ct) =>
        await GetAsync(ct).ConfigureAwait(false);

    public Task<UserSession?> GetAsync(CancellationToken ct) => ExecuteAsync(async () =>
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        return IsFresh(Current) ? Current : await RefreshCoreAsync(ct).ConfigureAwait(false);
    }, ct);

    public Task<UserSession?> RefreshAsync(CancellationToken ct) => ExecuteAsync(async () =>
    {
        await EnsureRestoredAsync(ct).ConfigureAwait(false);
        return await RefreshCoreAsync(ct).ConfigureAwait(false);
    }, ct);

    public Task<UserSession?> SignInAsync(CancellationToken ct) => ExecuteAsync(async () =>
    {
        var session = await browserLogin.TryLoginAsync(ct).ConfigureAwait(false);
        if (session is null) return null;
        _restored = true;
        await CommitAsync(session).ConfigureAwait(false);
        return session;
    }, ct);

    public async Task SignOutAsync(CancellationToken ct) => await ExecuteAsync(async () =>
    {
        await store.DeleteAsync(ct).ConfigureAwait(false);
        _restored = true;
        Volatile.Write(ref _current, null);
        return null;
    }, ct).ConfigureAwait(false);

    private async Task EnsureRestoredAsync(CancellationToken ct)
    {
        if (_restored) return;
        var session = await store.LoadAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _current, session);
        _restored = true;
    }

    private bool IsFresh(UserSession? session) =>
        session?.ExpiresAt is { } expiry && expiry > timeProvider.GetUtcNow() + RefreshWindow;

    private async Task<UserSession?> RefreshCoreAsync(CancellationToken ct)
    {
        var session = Current;
        if (session is null) return null;
        if (string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            if (session.ExpiresAt > timeProvider.GetUtcNow()) return session;
            await ClearInvalidSessionAsync().ConfigureAwait(false);
            return null;
        }

        // Transport failures and cancellation propagate without deleting the persisted session.
        var refreshed = await oauthClient.RefreshAsync(session, ct).ConfigureAwait(false);
        if (refreshed is null)
        {
            await ClearInvalidSessionAsync().ConfigureAwait(false);
            return null;
        }
        await CommitAsync(refreshed).ConfigureAwait(false);
        return refreshed;
    }

    private async Task CommitAsync(UserSession session)
    {
        // Rotation has already happened remotely; cancellation must not discard the new refresh token.
        Volatile.Write(ref _current, session);
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ClearInvalidSessionAsync()
    {
        Volatile.Write(ref _current, null);
        await store.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<UserSession?> ExecuteAsync(Func<Task<UserSession?>> operation, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        var previous = Current;
        try
        {
            ct.ThrowIfCancellationRequested();
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            var changed = previous != Current;
            _semaphore.Release();
            // Subscribers may read the manager again; never invoke them while holding the semaphore.
            if (changed) SessionChanged?.Invoke();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
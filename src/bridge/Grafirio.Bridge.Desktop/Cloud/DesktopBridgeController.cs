using Grafirio.Bridge.Desktop.Authentication;

namespace Grafirio.Bridge.Desktop.Cloud;

public sealed class DesktopBridgeController : IDesktopBridgeController
{
    private const string ConnectionFailureMessage = "Veri bağlantısı kurulamadı. Lütfen yeniden deneyin.";
    private readonly IDesktopSessionManager _sessions;
    private readonly Func<BridgeOptions, ILoggerFactory, DesktopBridgeDisplay, IDesktopBridgeHost> _createHost;
    private readonly IOptions<BridgeOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DesktopBridgeController> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Lock _connectionLock = new();
    private readonly DesktopBridgeDisplay _display;
    private CancellationTokenSource? _connectionCancellation;
    private IDesktopBridgeHost? _host;
    private string? _identityDirectory;
    private bool _disposed;

    public DesktopBridgeController(IDesktopSessionManager sessions,
        IOptions<BridgeOptions> options, ILoggerFactory loggerFactory)
        : this(sessions, options, loggerFactory, DesktopBridgeHost.Create) { }

    internal DesktopBridgeController(IDesktopSessionManager sessions, IOptions<BridgeOptions> options,
        ILoggerFactory loggerFactory,
        Func<BridgeOptions, ILoggerFactory, DesktopBridgeDisplay, IDesktopBridgeHost> createHost)
    {
        _sessions = sessions;
        _createHost = createHost;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DesktopBridgeController>();
        _display = new DesktopBridgeDisplay(loggerFactory.CreateLogger<DesktopBridgeDisplay>());
    }

    public event Action<BridgeStatus, string?>? Changed
    {
        add => _display.Changed += value;
        remove => _display.Changed -= value;
    }

    public async Task ConnectAsync(UserSession session, CancellationToken cancellationToken) =>
        await WithConnectionAsync(session, requireContext: false, cancellationToken).ConfigureAwait(false);

    public Task<Guid> GetConnectionContextAsync(UserSession session, CancellationToken cancellationToken) =>
        WithConnectionAsync(session, requireContext: true, cancellationToken);

    private async Task<Guid> WithConnectionAsync(UserSession session, bool requireContext,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_connectionLock) _connectionCancellation = connectionCancellation;
            var connectionToken = connectionCancellation.Token;
            connectionToken.ThrowIfCancellationRequested();
            var directory = ValidateSession(session);
            if (_host is not null && directory == _identityDirectory)
            {
                if (requireContext) return ReadConnectionContext(session);
                if (_host.IsConnected) return Guid.Empty;
            }

            await StopCoreAsync().ConfigureAwait(false);
            connectionToken.ThrowIfCancellationRequested();
            var options = DesktopBridgeOptions.Create(_options.Value, directory);
            var display = new DesktopBridgeDisplay(_loggerFactory.CreateLogger<DesktopBridgeDisplay>());
            display.Changed += _display.Publish;
            _host = _createHost(options, _loggerFactory, display);
            await _host.StartAsync(session, connectionToken).ConfigureAwait(false);
            connectionToken.ThrowIfCancellationRequested();
            ValidateSession(session);
            if (!_host.IsConnected) throw new DesktopBridgeException(ConnectionFailureMessage);
            _identityDirectory = directory;
            _logger.LogInformation("Desktop cloud bridge started.");
            return requireContext ? ReadConnectionContext(session) : Guid.Empty;
        }
        catch (OperationCanceledException) when (connectionCancellation.IsCancellationRequested)
        {
            await StopCoreAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            await StopCoreAsync().ConfigureAwait(false);
            _logger.LogWarning("Desktop cloud bridge connection failed ({ErrorType}).", exception.GetType().Name);
            var failure = exception as DesktopBridgeException ?? new DesktopBridgeException(ConnectionFailureMessage);
            _display.Publish(BridgeStatus.Disconnected, failure.Message);
            throw failure;
        }
        finally
        {
            lock (_connectionLock) _connectionCancellation = null;
            _semaphore.Release();
        }
    }

    private string ValidateSession(UserSession session)
    {
        var current = _sessions.Current;
        if (current is null || current.ExpiresAt is not { } expiry || expiry <= DateTimeOffset.UtcNow)
            throw new DesktopBridgeException("Oturum gerekli. Lütfen yeniden giriş yapın.");
        var directory = DesktopBridgeIdentity.GetDirectory(session);
        if (!ReferenceEquals(current, session) || directory != DesktopBridgeIdentity.GetDirectory(current))
            throw new DesktopBridgeException("Oturum değişti. Lütfen yeniden deneyin.");
        return directory;
    }

    private Guid ReadConnectionContext(UserSession session)
    {
        if (ValidateSession(session) != _identityDirectory || _host is not { IsConnected: true }
            || _host.BridgeId is not { } bridgeId || bridgeId == Guid.Empty)
            throw new DesktopBridgeException(ConnectionFailureMessage);
        return bridgeId;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancelPendingConnection();
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        var host = _host;
        _host = null;
        _identityDirectory = null;
        try
        {
            if (host is not null) await host.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError("Desktop cloud bridge shutdown failed ({ErrorType}).", exception.GetType().Name);
            throw new DesktopBridgeException("Bulut bağlantısı durdurulamadı. Lütfen uygulamayı kapatın.");
        }
        finally
        {
            _display.ShowStatus(BridgeStatus.Stopped);
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancelPendingConnection();
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            // Keep the semaphore alive for callers already waiting during disposal.
            _semaphore.Release();
        }
    }

    private void CancelPendingConnection()
    {
        // Sign-out interrupts enrollment or startup before waiting for the lifecycle lock.
        lock (_connectionLock) _connectionCancellation?.Cancel();
    }
}
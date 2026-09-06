using System.IO;
using System.Text.Json;
using Grafirio.Bridge.Desktop.Authentication;
using Grafirio.Bridge.Desktop.Cloud;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Grafirio.Bridge.Desktop.Shell;

public sealed class CloudPanel : System.Windows.Controls.UserControl, IDisposable
{
    private const int MaxMessageLength = 2048;
    private static readonly JsonSerializerOptions MessageJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    private readonly WebView2 _webView = new() { AllowExternalDrop = false };
    private readonly IDesktopSessionManager _sessions;
    private readonly IDesktopBridgeController _bridge;
    private readonly ILogger<CloudPanel> _logger;
    private readonly PanelOrigin _origin;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _initialization;
    private bool _ready;
    private bool _disposed;
    private long _navigation;

    public event Action? SignInRequested;
    public event Action? SignOutRequested;
    public event Action<string>? Problem;

    public CloudPanel(IDesktopSessionManager sessions, IDesktopBridgeController bridge, IOptions<BridgeOptions> options,
        ILogger<CloudPanel> logger)
    {
        _sessions = sessions;
        _bridge = bridge;
        _logger = logger;
        _origin = new PanelOrigin(options.Value.PanelUrl);
        Content = _webView;
    }

    public Task OpenAsync() => _initialization ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(DesktopPaths.DataDirectory, "CloudWebView2"));
            if (_disposed) return;
            await _webView.EnsureCoreWebView2Async(environment);
            if (_disposed) return;
            var core = _webView.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
            core.NavigationStarting += OnNavigationStarting;
            core.FrameNavigationStarting += (_, args) =>
            {
                if (!_origin.Contains(args.Uri)) args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                OpenExternal(args.Uri);
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                    Problem?.Invoke("Grafirio'ya ulaşılamadı. İnternet bağlantınızı kontrol edip yeniden deneyin.");
            };
            core.WebMessageReceived += OnWebMessageReceived;
            // Only a non-secret marker is installed; tokens use the validated message channel.
            var origin = JsonSerializer.Serialize(_origin.Address.GetLeftPart(UriPartial.Authority));
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                $"if(window === window.top && location.origin === {origin}) window.__GRAFIRIO_DESKTOP__ = true;");
            core.Navigate(_origin.Address.AbsoluteUri);
        }
        catch
        {
            _initialization = null;
            throw;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!_origin.Contains(args.Uri))
        {
            args.Cancel = true;
            if (args.IsUserInitiated) OpenExternal(args.Uri);
            else Problem?.Invoke("Bulut oturumu gerekli. Giriş Yap düğmesini kullanın.");
            return;
        }
        _ready = false;
        _navigation++;
    }

    private void OpenExternal(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http")) return;
        try { Browser.Open(address); }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "External browser could not be opened");
            Problem?.Invoke("Varsayılan tarayıcı açılamadı.");
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_disposed || !_origin.Contains(args.Source) ||
            !_origin.Contains(_webView.CoreWebView2.Source) || args.WebMessageAsJson.Length > MaxMessageLength) return;
        var navigation = _navigation;
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
            switch (type.GetString())
            {
                case "signIn": SignInRequested?.Invoke(); return;
                case "signOut": SignOutRequested?.Invoke(); return;
                case "sessionExpired": return;
                case "connectionContext":
                    if (!_ready || !root.TryGetProperty("requestId", out var contextIdentifier) ||
                        contextIdentifier.ValueKind != JsonValueKind.String ||
                        !Guid.TryParse(contextIdentifier.GetString(), out _)) return;
                    await ReplyConnectionContextAsync(contextIdentifier.GetString()!, navigation);
                    return;
                case "ready":
                case "refresh":
                    if (!root.TryGetProperty("requestId", out var identifier) ||
                        identifier.ValueKind != JsonValueKind.String ||
                        !Guid.TryParse(identifier.GetString(), out _)) return;
                    requestId = identifier.GetString();
                    _ready = true;
                    var session = type.GetString() == "refresh"
                        ? await _sessions.RefreshAsync(_lifetime.Token)
                        : await _sessions.GetAsync(_lifetime.Token);
                    if (navigation == _navigation) ReplyTokens(session, requestId);
                    return;
            }
        }
        catch (Exception exception) when (requestId is null && exception is JsonException or InvalidOperationException)
        {
            _logger.LogWarning("Invalid desktop panel message rejected");
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Desktop session request failed with {ErrorType}", exception.GetType().Name);
            if (!_disposed && navigation == _navigation && requestId is not null)
                Send(new { type = "tokens", requestId, error = "offline" });
        }
    }

    private async Task ReplyConnectionContextAsync(string requestId, long navigation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        deadline.CancelAfter(ConnectionContextRequest.Timeout);
        ConnectionContextResponse response;
        try
        {
            response = await ConnectionContextRequest.ResolveAsync(requestId, _sessions, _bridge, deadline.Token)
                .WaitAsync(deadline.Token);
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Desktop connection context failed with {ErrorType}", exception.GetType().Name);
            response = new(requestId, Error: _sessions.Current is null ? "signedOut" : "unavailable");
        }
        if (_disposed || navigation != _navigation) return;
        if (response.BridgeId is not null && (!ReferenceEquals(response.Session, _sessions.Current)
            || response.Session?.ExpiresAt <= DateTimeOffset.UtcNow))
            response = new(requestId, Error: "signedOut");
        Send(response);
    }

    public void PublishSession()
    {
        if (!_ready) return;
        if (_sessions.Current is { } session) ReplyTokens(session, null);
        else Send(new { type = "sessionExpired" });
    }

    private void ReplyTokens(UserSession? session, string? requestId)
    {
        if (session is null) Send(new { type = "tokens", requestId, error = "signedOut" });
        else Send(new
        {
            type = "tokens", requestId,
            tokens = new { token = session.AccessToken, idToken = session.IdToken }
        });
    }

    private void Send(object message)
    {
        if (!_disposed && _ready && _origin.Contains(_webView.CoreWebView2.Source))
            _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, MessageJsonOptions));
    }

    public void Reload()
    {
        if (_webView.CoreWebView2 is not null) _webView.CoreWebView2.Reload();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _webView.Dispose();
        _lifetime.Dispose();
    }
}
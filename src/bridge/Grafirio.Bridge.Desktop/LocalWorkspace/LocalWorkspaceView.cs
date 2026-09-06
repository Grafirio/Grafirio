using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;
using Grafirio.QueryPolicy;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Grafirio.Bridge.Desktop.LocalWorkspace;

public sealed class LocalWorkspaceView : System.Windows.Controls.UserControl, IDisposable
{
    private readonly WorkspaceMessageDispatcher _dispatcher;
    private readonly ILogger<LocalWorkspaceView> _logger;
    private readonly WebView2 _webView = new() { AllowExternalDrop = false };
    private readonly WorkspaceDeactivation _deactivation = new();
    private Task? _deactivationTask;
    private bool _initializationStarted;
    private bool _disposed;
    private bool _navigationStarted;
    private bool _documentReady;

    public LocalWorkspaceView(WorkspaceMessageDispatcher dispatcher, ILogger<LocalWorkspaceView> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        Content = _webView;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_initializationStarted || _disposed) return;
        _initializationStarted = true;
        try
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Grafirio", "Desktop", "LocalWorkspaceWebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (_disposed) return;
            await _webView.EnsureCoreWebView2Async(environment);
            if (_disposed) return;
            Configure(_webView.CoreWebView2);
            _webView.CoreWebView2.Navigate(WorkspaceProtocol.DocumentUrl);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Local workspace WebView initialization failed");
            if (!_disposed)
                Content = new TextBlock
                {
                    Text = "Yerel çalışma alanı başlatılamadı. WebView2 Runtime ve paketli arayüz kaynaklarını kontrol edin.",
                    Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap
                };
        }
    }

    private void Configure(CoreWebView2 core)
    {
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.NavigationStarting += (_, args) =>
        {
            if (_navigationStarted || !WorkspaceProtocol.IsDocument(args.Uri)) { args.Cancel = true; return; }
            _navigationStarted = true;
        };
        core.NavigationCompleted += (_, args) => _documentReady = args.IsSuccess;
        core.FrameNavigationStarting += (_, args) => args.Cancel = true;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnResourceRequested;
        core.WebMessageReceived += OnWebMessageReceived;
    }

    private void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var core = _webView.CoreWebView2;
        var asset = WorkspaceAssets.Find(args.Request.Uri);
        if (args.Request.Method != "GET" || asset is null)
        {
            args.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 403, "Forbidden", "");
            return;
        }
        var stream = typeof(LocalWorkspaceView).Assembly.GetManifestResourceStream(asset.Value.ResourceName);
        if (stream is null)
        {
            _logger.LogError("Packaged local workspace resource missing: {ResourceName}", asset.Value.ResourceName);
            stream = new MemoryStream(Encoding.UTF8.GetBytes("Packaged workspace resource missing."));
            args.Response = core.Environment.CreateWebResourceResponse(stream, 404, "Not Found", "Content-Type: text/plain");
            return;
        }
        args.Response = core.Environment.CreateWebResourceResponse(stream, 200, "OK",
            "Content-Type: " + asset.Value.ContentType + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
            "Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; " +
            "img-src 'none'; connect-src 'none'; font-src 'none'; object-src 'none'; frame-src 'none'; " +
            "base-uri 'none'; form-action 'none'; frame-ancestors 'none'\r\n");
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_disposed || !WorkspaceProtocol.IsDocument(args.Source) ||
            !WorkspaceProtocol.IsDocument(_webView.CoreWebView2.Source)) return;
        // Module scripts can start RPC before NavigationCompleted is delivered.
        _documentReady = true;
        WorkspaceMessage message;
        try
        {
            if (_deactivation.TryAcknowledge(args.Source, args.WebMessageAsJson)) return;
            message = WorkspaceProtocol.Parse(args.Source, args.WebMessageAsJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            _logger.LogWarning("Invalid local workspace message rejected");
            return;
        }
        try
        {
            var result = await _dispatcher.DispatchAsync(message);
            Reply(new { id = message.Id, ok = true, result });
        }
        catch (Exception exception)
        {
            // Provider exception messages can contain server details or SQL; never forward them to the browser.
            _logger.LogWarning("Local workspace method {Method} failed with {ErrorType}", message.Method, exception.GetType().Name);
            var error = exception switch
            {
                OperationCanceledException => "İşlem iptal edildi veya zaman sınırına ulaştı.",
                QueryPolicyException => "SQL güvenlik politikası reddetti. Salt-okunur kullanıcı, şema.tablo izinleri ve fiziksel tablo koşullarını kontrol edin.",
                ArgumentException => "Girdi geçersiz. Alanları, seçili bağlantıyı ve izin listesini kontrol edin.",
                NotSupportedException => "Bu sağlayıcıda serbest SQL kapalıdır; bağlantı testi ve şema keşfi desteklenir.",
                _ => "İşlem tamamlanamadı. Bağlantı, TLS sertifikası, salt-okunur kullanıcı yetkileri ve işlem sınırlarını kontrol edin."
            };
            Reply(new { id = message.Id, ok = false, error });
        }
    }

    private void Reply(object response)
    {
        if (!_disposed && WorkspaceProtocol.IsDocument(_webView.CoreWebView2.Source))
            _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response, WorkspaceProtocol.JsonOptions));
    }

    /// <summary>
    /// Must be awaited on the UI thread before collapsing this view or gracefully disposing the shell.
    /// Cancels database work, then waits up to 15 seconds for draft persistence and dialog closure.
    /// Failure leaves the caller responsible for keeping the workspace visible and allowing a retry.
    /// Concurrent calls share the same task; subsequent calls flush any newer work.
    /// </summary>
    public Task DeactivateAsync()
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_deactivationTask is { IsCompleted: false }) return _deactivationTask;
        return _deactivationTask = DeactivateCoreAsync();
    }

    private async Task DeactivateCoreAsync()
    {
        try
        {
            _dispatcher.BeginDeactivation();
            if (_documentReady)
                await _deactivation.FlushAsync(Reply, WorkspaceDeactivation.FlushTimeout);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Local workspace deactivation failed; pending work must be retained");
            throw;
        }
        finally { _dispatcher.EndDeactivation(); }
    }

    /// <summary>Releases resources only; call and await DeactivateAsync first to persist pending work.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Dispatcher.VerifyAccess();
        _disposed = true;
        Loaded -= OnLoaded;
        _deactivation.Abort();
        _dispatcher.Dispose();
        _webView.Dispose();
    }
}
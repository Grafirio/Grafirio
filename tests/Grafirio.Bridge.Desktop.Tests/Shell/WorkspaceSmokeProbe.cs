using System.IO;
using System.Text.Json;
using Grafirio.Bridge.Desktop.LocalWorkspace;
using Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

internal sealed class WorkspaceSmokeProbe : IDisposable
{
    public static readonly string[] AssetNames =
        ["index.html", "workspace.css", "app.js", "channel.js", "connections.js", "results.js"];
    private const string ReadyEvent = "workspace-smoke-ready";
    private const int SuccessStatus = 200;
    private const int ForbiddenStatus = 403;
    private readonly WebView2 _webView;
    private readonly string _profile;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _navigated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _resourcesLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _browserExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<JsonElement> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _flushed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<string> _loadedAssets = new(StringComparer.Ordinal);
    private readonly List<string> _unexpectedRequests = [];
    private CoreWebView2? _core;
    private CoreWebView2Environment? _environment;

    public WorkspaceSmokeProbe(WebView2 webView, string profile)
    {
        _webView = webView;
        _profile = profile;
        webView.CoreWebView2InitializationCompleted += OnInitialized;
    }

    public async Task WaitForNavigationAsync(CancellationToken cancellationToken)
    {
        await _initialized.Task.WaitAsync(cancellationToken);
        await _navigated.Task.WaitAsync(cancellationToken);
        Assert.Equal(WorkspaceProtocol.DocumentUrl, _core!.Source);
    }

    public async Task<JsonElement> ReadReadyStateAsync(CancellationToken cancellationToken)
    {
        // Check immediately as well as observing mutations: the native load reply can precede navigation completion.
        var script = $$"""
            (() => {
                const observer = new MutationObserver(check);
                function check() {
                    const status = document.getElementById('status');
                    const editor = document.getElementById('sql');
                    if (status?.textContent !== 'Yerel çalışma alanı hazır.' || !editor || editor.disabled) return;
                    observer.disconnect();
                    window.chrome.webview.postMessage({
                        event: '{{ReadyEvent}}',
                        title: document.title,
                        heading: document.querySelector('main h1')?.textContent,
                        draft: editor.value,
                        styled: [...document.styleSheets].some(sheet =>
                            sheet.href === new URL('workspace.css', location.href).href && sheet.cssRules.length > 0)
                    });
                }
                observer.observe(document.documentElement, { subtree: true, childList: true, attributes: true, characterData: true });
                check();
            })()
            """;
        await _webView.ExecuteScriptAsync(script).WaitAsync(cancellationToken);
        return await _ready.Task.WaitAsync(cancellationToken);
    }

    public async Task AssertResourcesAsync(CancellationToken cancellationToken)
    {
        await _flushed.Task.WaitAsync(cancellationToken);
        await _resourcesLoaded.Task.WaitAsync(cancellationToken);
        Assert.Empty(_unexpectedRequests);
        foreach (var name in AssetNames) Assert.Contains(WorkspaceProtocol.Origin + "/" + name, _loadedAssets);
    }

    public async Task WaitForBrowserExitAsync(CancellationToken cancellationToken)
    {
        if (_environment is not null) await _browserExited.Task.WaitAsync(cancellationToken);
    }

    private void OnInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            _initialized.TrySetException(new InvalidOperationException(
                "WebView2 initialization failed. Install the WebView2 Runtime on this Windows test agent.",
                args.InitializationException));
            return;
        }

        try
        {
            _core = _webView.CoreWebView2;
            _environment = _core.Environment;
            _environment.BrowserProcessExited += OnBrowserExited;
            Assert.Equal(Path.GetFullPath(_profile), Path.GetFullPath(_environment.UserDataFolder), ignoreCase: true);
            _core.NavigationCompleted += OnNavigationCompleted;
            _core.WebMessageReceived += OnWebMessageReceived;
            _core.WebResourceRequested += OnResourceRequested;
            _core.WebResourceResponseReceived += OnResourceResponseReceived;
            _core.ProcessFailed += OnProcessFailed;
            _initialized.TrySetResult();
        }
        catch (Exception exception)
        {
            _initialized.TrySetException(exception);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess) _navigated.TrySetResult();
        else _navigated.TrySetException(new InvalidOperationException($"Local navigation failed: {args.WebErrorStatus}."));
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!WorkspaceProtocol.IsDocument(args.Source)) return;
        using var message = JsonDocument.Parse(args.WebMessageAsJson);
        if (!message.RootElement.TryGetProperty("event", out var eventName)) return;
        if (eventName.GetString() == ReadyEvent)
            _ready.TrySetResult(message.RootElement.Clone());
        if (eventName.GetString() == "workspace-flushed" && message.RootElement.GetProperty("ok").GetBoolean())
            _flushed.TrySetResult();
    }

    private void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (args.Request.Method == "GET" && WorkspaceAssets.Find(args.Request.Uri) is not null) return;
        _unexpectedRequests.Add(args.Request.Uri);
        args.Response = _environment!.CreateWebResourceResponse(Stream.Null, ForbiddenStatus, "Forbidden", "");
    }

    private void OnResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (WorkspaceAssets.Find(args.Request.Uri) is null) return;
        if (args.Response.StatusCode != SuccessStatus)
        {
            _resourcesLoaded.TrySetException(new InvalidOperationException(
                $"Packaged resource failed: {args.Request.Uri}, HTTP {args.Response.StatusCode}."));
            return;
        }
        _loadedAssets.Add(args.Request.Uri);
        if (_loadedAssets.Count == AssetNames.Length) _resourcesLoaded.TrySetResult();
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args)
    {
        var exception = new InvalidOperationException($"WebView2 process failed: {args.ProcessFailedKind}.");
        _navigated.TrySetException(exception);
        _ready.TrySetException(exception);
        _resourcesLoaded.TrySetException(exception);
        _flushed.TrySetException(exception);
    }

    private void OnBrowserExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs args) =>
        _browserExited.TrySetResult();

    public void Dispose()
    {
        _webView.CoreWebView2InitializationCompleted -= OnInitialized;
        if (_environment is not null) _environment.BrowserProcessExited -= OnBrowserExited;
        // Core event subscriptions die with the disposed WebView; accessing that RCW after disposal is invalid.
    }
}
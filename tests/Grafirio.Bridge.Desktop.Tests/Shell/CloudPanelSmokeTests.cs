using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Grafirio.Bridge.Desktop.Shell;
using Grafirio.Bridge.Desktop.Tests.Cloud;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

[Collection(CloudSmokeCollection.Name)]
public sealed class CloudPanelSmokeTests
{
    private const string FixtureUrl = "http://127.0.0.1:49157/";
    private const string ProfileVariable = "WEBVIEW2_USER_DATA_FOLDER";
    private const string FixtureHtml = "<!doctype html><html><head><title>Grafirio cloud fixture</title></head>" +
        "<body><h1>Existing website fixture</h1><script>" +
        "window.chrome.webview.addEventListener('message', e => {" +
        "window.chrome.webview.postMessage({type:'fixtureReply',response:e.data}); });" +
        "</script></body></html>";

    [Fact]
    [Trait("Category", "WindowsWebView2Smoke")]
    public async Task RealWebViewServesCloudContextAndKeepsOriginNavigationAndSignOutGates()
    {
        Assert.True(OperatingSystem.IsWindows() && Environment.UserInteractive,
            "An interactive Windows desktop and the WebView2 runtime are required.");
        var previousProfile = Environment.GetEnvironmentVariable(ProfileVariable);
        var profile = Path.Combine(Path.GetTempPath(), "Grafirio.CloudSmoke", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(ProfileVariable, profile);
            await CloudSmokeSta.RunAsync(token => ExerciseAsync(profile, token));
        }
        finally { Environment.SetEnvironmentVariable(ProfileVariable, previousProfile); }
    }

    private static async Task ExerciseAsync(string profile, CancellationToken cancellationToken)
    {
        var sessions = new ContextSessionManager();
        sessions.Set(ContextSessionManager.CreateSession());
        var bridge = new ContextBridgeController();
        using var panel = new CloudPanel(sessions, bridge, Options.Create(new BridgeOptions { PanelUrl = FixtureUrl }),
            NullLogger<CloudPanel>.Instance);
        var webView = Assert.IsType<WebView2>(panel.Content);
        var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browserExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replies = new Dictionary<string, TaskCompletionSource<JsonElement>>();
        var received = new List<JsonElement>();
        webView.CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (!args.IsSuccess) { navigation.TrySetException(args.InitializationException); return; }
            var core = webView.CoreWebView2;
            core.Environment.BrowserProcessExited += (_, _) => browserExit.TrySetResult();
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, request) =>
            {
                // Every request is fulfilled in memory; this fixture never contacts production or a login server.
                request.Response = core.Environment.CreateWebResourceResponse(
                    new MemoryStream(Encoding.UTF8.GetBytes(FixtureHtml)), 200, "OK", "Content-Type: text/html");
            };
            core.NavigationCompleted += (_, completed) =>
            {
                if (completed.IsSuccess) navigation.TrySetResult();
                else navigation.TrySetException(new InvalidOperationException(completed.WebErrorStatus.ToString()));
            };
            core.WebMessageReceived += (_, message) =>
            {
                using var document = JsonDocument.Parse(message.WebMessageAsJson);
                if (document.RootElement.GetProperty("type").GetString() != "fixtureReply") return;
                var response = document.RootElement.GetProperty("response").Clone();
                received.Add(response);
                if (response.TryGetProperty("requestId", out var requestId)
                    && requestId.ValueKind == JsonValueKind.String
                    && replies.TryGetValue(requestId.GetString()!, out var completion))
                    completion.TrySetResult(response);
            };
        };
        panel.SignOutRequested += () => { sessions.Set(null); panel.PublishSession(); };
        var window = new Window { Content = panel, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show();
            await panel.OpenAsync().WaitAsync(cancellationToken);
            await navigation.Task.WaitAsync(cancellationToken);
            Assert.Equal("true", await webView.ExecuteScriptAsync("window.__GRAFIRIO_DESKTOP__ === true"));
            Assert.False(webView.CoreWebView2.Settings.AreHostObjectsAllowed);
            Assert.False(webView.CoreWebView2.Settings.AreDevToolsEnabled);

            await webView.ExecuteScriptAsync($"window.chrome.webview.postMessage({JsonSerializer.Serialize(new { type = "connectionContext", requestId = Guid.NewGuid().ToString() })})");

            async Task<JsonElement> RequestAsync(string type)
            {
                var requestId = Guid.NewGuid().ToString();
                var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                replies.Add(requestId, completion);
                await webView.ExecuteScriptAsync($"window.chrome.webview.postMessage({JsonSerializer.Serialize(new { type, requestId })})");
                return await completion.Task.WaitAsync(cancellationToken);
            }

            Assert.Equal("tokens", (await RequestAsync("ready")).GetProperty("type").GetString());
            Assert.Equal(1, bridge.ContextCalls);
            await webView.ExecuteScriptAsync("window.chrome.webview.postMessage({type:'connectionContext',requestId:'invalid'})");
            await RequestAsync("ready");
            Assert.Equal(1, bridge.ContextCalls);
            var context = await RequestAsync("connectionContext");
            Assert.Equal(bridge.BridgeId, context.GetProperty("bridgeId").GetGuid());
            Assert.Equal(3, context.EnumerateObject().Count());

            bridge.Resolve = (_, _) => throw new InvalidOperationException("private fixture detail");
            Assert.Equal("unavailable", (await RequestAsync("connectionContext")).GetProperty("error").GetString());

            var pending = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bridge.Resolve = (_, _) => { started.TrySetResult(); return pending.Task; };
            var staleId = Guid.NewGuid().ToString();
            await webView.ExecuteScriptAsync($"window.chrome.webview.postMessage({JsonSerializer.Serialize(new { type = "connectionContext", requestId = staleId })})");
            await started.Task.WaitAsync(cancellationToken);
            navigation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            panel.Reload();
            await navigation.Task.WaitAsync(cancellationToken);
            await RequestAsync("ready");
            pending.SetResult(bridge.BridgeId);
            await RequestAsync("ready");
            Assert.DoesNotContain(received, response => response.TryGetProperty("requestId", out var id) && id.GetString() == staleId);

            var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (args.Uri == "https://untrusted.example/")
                {
                    Assert.True(args.Cancel);
                    blocked.TrySetResult();
                }
            };
            webView.CoreWebView2.Navigate("https://untrusted.example/");
            await blocked.Task.WaitAsync(cancellationToken);
            Assert.StartsWith(FixtureUrl, webView.CoreWebView2.Source);
            await webView.ExecuteScriptAsync("window.chrome.webview.postMessage({type:'signOut'})");
            var signedOut = await RequestAsync("connectionContext");
            Assert.Equal("signedOut", signedOut.GetProperty("error").GetString());
            Assert.False(signedOut.TryGetProperty("bridgeId", out _));
        }
        finally
        {
            var initialized = webView.CoreWebView2 is not null;
            panel.Dispose();
            window.Close();
            if (initialized) await browserExit.Task.WaitAsync(cancellationToken);
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        }
    }
}
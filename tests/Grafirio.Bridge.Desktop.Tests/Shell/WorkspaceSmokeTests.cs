using System.IO;
using System.Text.Json;
using System.Windows;
using Grafirio.Bridge.Desktop.LocalWorkspace;
using Grafirio.Bridge.Desktop.LocalWorkspace.Data;
using Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;
using Grafirio.Bridge.Desktop.LocalWorkspace.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Wpf;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

[Collection(WorkspaceSmokeCollection.Name)]
public sealed class WorkspaceSmokeTests
{
    private const string InitialDraft = "SELECT 17 AS InitialSmokeDraft";
    private const string EditedDraft = "SELECT 29 AS PersistedSmokeDraft";
    private const string ProfileVariable = "WEBVIEW2_USER_DATA_FOLDER";
    private const string ArgumentsVariable = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";
    private const string OfflineArguments = "--disable-background-networking --disable-component-update " +
        "--disable-sync --no-first-run --no-proxy-server --host-resolver-rules=\"MAP * ~NOTFOUND\"";
    private const double WindowWidth = 1100;
    private const double WindowHeight = 760;

    [Fact]
    [Trait("Category", "WindowsWebView2Smoke")]
    public async Task PackagedWorkspaceLoadsAndDeactivationPersistsSqlDraft()
    {
        Assert.True(OperatingSystem.IsWindows(), "This smoke test requires Windows; it must not be skipped.");
        Assert.True(Environment.UserInteractive, "This smoke test requires an interactive Windows desktop.");
        AssertPackagedResources();

        var profile = Path.Combine(Path.GetTempPath(), "Grafirio.WorkspaceSmoke", Guid.NewGuid().ToString("N"));
        var previousProfile = Environment.GetEnvironmentVariable(ProfileVariable);
        var previousArguments = Environment.GetEnvironmentVariable(ArgumentsVariable);
        try
        {
            // The documented environment override isolates the production view's fixed profile path.
            Environment.SetEnvironmentVariable(ProfileVariable, profile);
            Environment.SetEnvironmentVariable(ArgumentsVariable, OfflineArguments);
            await WorkspaceSmokeSta.RunAsync((operationToken, cleanupToken) =>
                ExerciseWorkspaceAsync(profile, operationToken, cleanupToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProfileVariable, previousProfile);
            Environment.SetEnvironmentVariable(ArgumentsVariable, previousArguments);
        }
    }

    private static void AssertPackagedResources()
    {
        foreach (var name in WorkspaceSmokeProbe.AssetNames)
        {
            var asset = WorkspaceAssets.Find(WorkspaceProtocol.Origin + "/" + name);
            Assert.True(asset.HasValue, $"No resource mapping exists for {name}.");
            using var stream = typeof(LocalWorkspaceView).Assembly.GetManifestResourceStream(asset.Value.ResourceName);
            Assert.NotNull(stream);
            Assert.True(stream.Length > 0, $"Packaged resource {name} is empty.");
        }
    }

    private static async Task ExerciseWorkspaceAsync(
        string profile, CancellationToken operationToken, CancellationToken cleanupToken)
    {
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        Assert.Null(System.Windows.Application.Current);
        var store = new WorkspaceSmokeStore(InitialDraft);
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLocalWorkspace();
        services.Replace(ServiceDescriptor.Singleton<ILocalWorkspaceStore>(store));
        services.Replace(ServiceDescriptor.Singleton<ILocalDatabase>(new WorkspaceSmokeDatabase()));
        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<WorkspaceMessageDispatcher>();
        using var view = new LocalWorkspaceView(dispatcher, NullLogger<LocalWorkspaceView>.Instance);
        var webView = Assert.IsType<WebView2>(view.Content);
        using var probe = new WorkspaceSmokeProbe(webView, profile);
        var window = new Window
        {
            Title = "Grafirio local workspace smoke test",
            Width = WindowWidth,
            Height = WindowHeight,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = view
        };

        try
        {
            window.Show();
            Assert.True(window.IsVisible);
            await probe.WaitForNavigationAsync(operationToken);
            var state = await probe.ReadReadyStateAsync(operationToken);
            Assert.Equal("Grafirio · Yerel çalışma alanı", state.GetProperty("title").GetString());
            Assert.Equal("Sorgu çalışma alanı", state.GetProperty("heading").GetString());
            Assert.Equal(InitialDraft, state.GetProperty("draft").GetString());
            Assert.True(state.GetProperty("styled").GetBoolean(), "The packaged stylesheet did not load.");
            Assert.True(store.ReadCount > 0, "The native load RPC never read the injected store.");

            var editScript = $$"""
                (() => {
                    const editor = document.getElementById('sql');
                    if (!editor || editor.disabled) throw new Error('SQL editor is not ready.');
                    editor.value = {{JsonSerializer.Serialize(EditedDraft)}};
                    editor.dispatchEvent(new Event('input', { bubbles: true }));
                    return editor.value;
                })()
                """;
            var edited = await webView.ExecuteScriptAsync(editScript).WaitAsync(operationToken);
            Assert.Equal(EditedDraft, JsonSerializer.Deserialize<string>(edited));
            await view.DeactivateAsync().WaitAsync(operationToken);
            Assert.True(store.UpdateCount > 0, "Deactivation did not persist the SQL draft.");
            Assert.Equal(EditedDraft, store.Document.Draft);
            Assert.Null(store.Document.SelectedConnectionId);
            Assert.Empty(store.Document.Connections);
            await probe.AssertResourcesAsync(operationToken);
        }
        finally
        {
            try
            {
                view.Dispose();
            }
            finally
            {
                window.Hide();
                window.Close();
                await probe.WaitForBrowserExitAsync(cleanupToken);
                if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
            }
        }
    }
}
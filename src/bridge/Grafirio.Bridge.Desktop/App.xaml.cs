using System.IO;
using System.Windows;
using Grafirio.Bridge.Desktop.Authentication;
using Grafirio.Bridge.Desktop.Cloud;
using Grafirio.Bridge.Desktop.Shell;
using Serilog;

namespace Grafirio.Bridge.Desktop;

public partial class App : Application
{
    private IHost? _host;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private SingleInstance? _instance;
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new SingleInstance();
        if (!_instance.IsFirstInstance)
        {
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                optional: true, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));
            builder.Services.AddSerilog(configuration => configuration
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(Path.Combine(DesktopPaths.DataDirectory, "Logs", "desktop-.log"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7));
            builder.Services.AddDesktopAuthentication();
            builder.Services.AddDesktopBridge();
            builder.Services.AddSingleton<CloudPanel>();
            builder.Services.AddSingleton<MainWindow>();
            _host = builder.Build();
            await _host.StartAsync();
            _window = _host.Services.GetRequiredService<MainWindow>();
            _tray = new TrayIcon(_window, () => _ = ExitAsync());
            _instance.Listen(() => Dispatcher.BeginInvoke(() => _window.BringToFront()));
            _window.Show();
            _window.Start();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Desktop startup failed");
            MessageBox.Show("Grafirio başlatılamadı. Kurulumu onarın ve kullanıcı klasörü izinlerini kontrol edin.",
                "Grafirio", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            if (_window is not null) await _window.StopAsync();
            if (_host is not null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(10));
                if (_host is IAsyncDisposable disposable) await disposable.DisposeAsync();
                else _host.Dispose();
            }
        }
        catch (Exception exception) { Log.Warning(exception, "Desktop shutdown failed"); }
        finally
        {
            _tray?.Dispose();
            _instance?.Dispose();
            _instance = null;
            Shutdown();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _tray?.Dispose();
        _tray = null;
        base.OnSessionEnding(e);
    }
}
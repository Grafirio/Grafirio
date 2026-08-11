using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Pencereli kabuk.
///
/// Cekirdek burada da bir <see cref="IHost"/> icinde kosuyor — konsol
/// surumundekinin aynisi, <see cref="BridgeCore.AddBridgeCore"/> ile. Tek fark
/// <see cref="IBridgeDisplay"/>: kurulum kodu kutu icinde konsola degil,
/// pencerede buyuk puntoyla gosteriliyor ve tarayici kendiliginden aciliyor.
///
/// Neden ayri bir ikili: bridge sunucu odasina servis olarak kuruluyor ama
/// asil ihtiyac cogu zaman bir insanin kendi makinesinde — VPN istemeden,
/// cift tiklayip calistirdigi bir sey. Postman Desktop Agent'in yaptigi bu.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private WpfBridgeDisplay? _display;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _display = new WpfBridgeDisplay(Dispatcher);

        var builder = Host.CreateApplicationBuilder();

        // Yapilandirma exe'nin yaninda. Calisma dizini kisayoldan baslatinca
        // baska bir yer olabiliyor; gorece okumak dosyayi bulamamak demekti.
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));
        builder.Services.AddSingleton<IBridgeDisplay>(_display);
        builder.Services.AddBridgeCore();

        // Gunluk pencerede gosteriliyor: sorun cikmasi hâlinde kullaniciya
        // "ProgramData altindaki dosyaya bakin" demek yerine ekranda duruyor.
        builder.Logging.AddProvider(new WindowLogProvider(LogBuffer.Instance));

        _host = builder.Build();

        _window = new MainWindow(_display, _host.Services);
        _tray = new TrayIcon(_window, Shutdown);

        _display.StatusChanged += (status, _) => _tray.Update(status);

        _window.Show();

        await _host.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        if (_host is not null)
        {
            // Bridge'in kapanirken bulut baglantisini duzgunce kapatmasi
            // gerekiyor; aksi halde panel bridge'i bir sure daha "çevrimiçi"
            // gosterir.
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}

/// <summary>
/// Yapilandirma dosyasinin yolu — hata mesajlarinda gosteriliyor.
/// </summary>
public static class DesktopPaths
{
    public static string SettingsFile => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static string DataDirectory => BridgeCore.DataDirectory;
}

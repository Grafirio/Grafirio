using System.Windows;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Masaustu kabugu.
///
/// Cekirdek burada da konsol surumundekiyle ayni servislerle kosuyor
/// (<see cref="BridgeCore.AddBridgeCore"/>). Iki fark var: giris tarayicida
/// authorization code + PKCE ile yapiliyor (device flow servis surumunde
/// kaldi) ve isci kendiliginden baslamiyor — kullanici "Giriş Yap" dedikten
/// sonra basliyor.
///
/// Neden ayri bir ikili: agent sunucu odasina servis olarak da kuruluyor ama
/// asil ihtiyac cogu zaman bir insanin kendi makinesinde — VPN istemeden,
/// cift tiklayip calistirdigi bir sey.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private TrayIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();

        // Yapilandirma exe'nin yaninda. Calisma dizini kisayoldan baslatinca
        // baska bir yer olabiliyor; gorece okumak dosyayi bulamamak demekti.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(
            System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            optional: true,
            reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));

        // Kurulum kodu diye bir sey yok: giris tarayicida yapiliyor. Cekirdek
        // yine de bir IBridgeDisplay istiyor cunku servis surumu device
        // flow'u kullanmayi surduruyor.
        builder.Services.AddSingleton<IBridgeDisplay, SilentBridgeDisplay>();
        builder.Services.AddSingleton<BrowserLogin>();

        builder.Services.AddBridgeCore(runInBackground: false);

        _host = builder.Build();

        _window = new MainWindow(_host.Services);
        _tray = new TrayIcon(_window, Shutdown);

        _window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        if (_host is not null)
        {
            // Kapanirken bulut baglantisinin duzgunce kapanmasi gerekiyor;
            // aksi halde panel bridge'i bir sure daha "çevrimiçi" gosterir.
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}

/// <summary>
/// Masaustunde kurulum ekrani yok; durum bilgisi de gunluge yaziliyor. Bu
/// yuzden gosterilecek bir sey yok — ama cekirdegin arayuzu karsilanmali.
/// </summary>
public class SilentBridgeDisplay : IBridgeDisplay
{
    public void ShowDeviceCode(DeviceCodePrompt prompt) { }

    public void ShowStatus(BridgeStatus status, string? detail = null) { }
}

using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Uygulamanin tek penceresi ve iki hâli: giris ve panel.
///
/// Giris ekraninda logo ve tek bir dugme var. Once burada durum karti,
/// baglanti listesi ve gunluk vardi; hepsi kalkti. Kullanicinin ogrenmesi
/// gereken ikinci bir arayuz yaratmak, panelde kazandigi aliskanliklari bu
/// pencerede ise yaramaz hâle getiriyordu.
///
/// Giristen sonra panelin KENDISI aciliyor — yeniden yazilmis bir benzeri
/// degil. Benzeri yazilsaydi "web'dekiyle ayni" olmasi ilk degisiklige kadar
/// surerdi.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly BridgeOptions _options;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();

        _services = services;
        _options = services.GetRequiredService<IOptions<BridgeOptions>>().Value;

        Icon = BrandIcon.Mark();

        LogText.Text = string.Join(Environment.NewLine, LogBuffer.Instance.Snapshot());
        LogBuffer.Instance.LineAdded += line => Dispatcher.Invoke(() => AppendLog(line));
    }

    private void AppendLog(string line)
    {
        LogText.Text = LogText.Text.Length == 0
            ? line
            : LogText.Text + Environment.NewLine + line;

        LogScroller.ScrollToEnd();
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        Hint("Tarayıcıda giriş bekleniyor…");

        try
        {
            var session = await _services.GetRequiredService<BrowserLogin>()
                .TryLoginAsync(CancellationToken.None);

            if (session is null)
            {
                Hint("Giriş tamamlanamadı. Tekrar deneyebilirsiniz.", isFailure: true);
                return;
            }

            // Giris bitti; pencere one geliyor. Kullanici tarayicidayken
            // uygulamanin arkada kalmasi, "simdi ne olacak" sorusunu
            // doguruyordu.
            Activate();

            if (!await EnrollIfNeededAsync(session)) return;

            await StartAgentAsync();
            await ShowPanelAsync(session);
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<ILogger<MainWindow>>()
                .LogError(ex, "Giriş sırasında beklenmeyen hata.");

            Hint($"Giriş yapılamadı: {ex.Message}", isFailure: true);
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Ilk giriste bu makine sirkete tanitiliyor. Sonraki acilislarda kimlik
    /// zaten yerel durum dosyasinda ve bu adim atlaniyor — kayit makineye
    /// ait, oturuma degil.
    /// </summary>
    private async Task<bool> EnrollIfNeededAsync(UserSession session)
    {
        var state = _services.GetRequiredService<BridgeState>();
        state.Load();

        if (state.IsEnrolled) return true;

        Hint("Bu bilgisayar hesabınıza bağlanıyor…");

        var enrolled = await _services.GetRequiredService<BridgeEnrollment>()
            .EnrollWithTokenAsync(session.AccessToken, CancellationToken.None);

        if (!enrolled)
        {
            Hint("Bu bilgisayar hesabınıza bağlanamadı.", isFailure: true);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Veritabanina baglanan kisim. Panel acildiktan sonra da arka planda
    /// calismaya devam ediyor; pencerenin kapanmasi onu durdurmuyor.
    /// </summary>
    private Task StartAgentAsync() =>
        _services.GetRequiredService<BridgeWorker>().StartAsync(CancellationToken.None);

    /// <summary>
    /// Paneli acar ve oturumu devreder.
    ///
    /// Giris DIS TARAYICIDA yapildigi icin bu pencerenin Keycloak cerezi yok;
    /// panel kendi basina acilsaydi kullaniciyi ikinci kez giris yapmaya
    /// zorlardi. Token'lar sayfanin ilk betigi calismadan once enjekte
    /// ediliyor ve keycloak-js onlarla basliyor.
    /// </summary>
    private async Task ShowPanelAsync(UserSession session)
    {
        // Tarayici verisi kullanicinin profilinde: uygulamanin yanina
        // yazmak, Program Files altina kurulan bir uygulamada yazma izni
        // olmadigi icin sessizce basarisiz oluyor.
        var profile = Path.Combine(BridgeCore.DataDirectory, "webview");
        Directory.CreateDirectory(profile);

        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: profile);

        await Panel.EnsureCoreWebView2Async(environment);

        var handoff = JsonSerializer.Serialize(new
        {
            tokens = new
            {
                token = session.AccessToken,
                refreshToken = session.RefreshToken,
                idToken = session.IdToken,
            }
        });

        await Panel.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            $"window.__GRAFIRIO_DESKTOP__ = {handoff};");

        // Panel disina cikan baglantilar (dokuman, destek) uygulamanin icinde
        // acilmasin: kullanici uygulamanin icinde kaybolur ve geri donemez.
        Panel.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            Browser.Open(args.Uri);
        };

        Panel.Source = new Uri(_options.PanelUrl);

        LoginView.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Visible;
    }

    private void Hint(string text, bool isFailure = false)
    {
        LoginHint.Text = text;
        LoginHint.Visibility = Visibility.Visible;

        // Hata olunca gunluk kendiliginden aciliyor: sebebi gormek icin
        // kullanicinin once bir alani kesfetmesi gerekmemeli.
        if (isFailure) DetailsPanel.IsExpanded = true;
    }
}

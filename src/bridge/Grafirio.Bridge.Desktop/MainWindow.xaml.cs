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

    /// <summary>Bu makineyi sirkete baglama denemesinin sayisi.</summary>
    private const int ConnectAttempts = 3;

    /// <summary>
    /// Giris yapan kisinin oturumu. "Tekrar dene" bunu kullaniyor: kaydin
    /// basarisiz olmasi kullanicinin yeniden giris yapmasini gerektirmiyor.
    /// </summary>
    private UserSession? _session;

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

    /// <summary>
    /// Giris ve hemen ardindan panel.
    ///
    /// Bu makinenin sirkete kaydi ARTIK panelin onunde durmuyor. Duruyordu:
    /// kayit basarisiz olunca panel hic acilmiyordu ve giris yapmis kullanici
    /// bos bir pencereyle kaliyordu. Oysa panelin kayitla isi yok — kayit,
    /// bu makinedeki veritabanina sorgu gelebilmesi icin gerekli. Ikisini tek
    /// dugume baglamak, calisan bir seyi calismayan bir seyin rehinesi
    /// yapmakti.
    /// </summary>
    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        ClearProblem();
        Hint("Tarayıcıda giriş bekleniyor…");

        try
        {
            var session = await _services.GetRequiredService<BrowserLogin>()
                .TryLoginAsync(CancellationToken.None);

            if (session is null)
            {
                Hint("Giriş tamamlanamadı. Tekrar deneyebilirsiniz.");
                ShowProblem("Giriş tamamlanamadı.", canRetry: false);
                return;
            }

            _session = session;

            // Giris bitti; pencere one geliyor. Kullanici tarayicidayken
            // uygulamanin arkada kalmasi, "simdi ne olacak" sorusunu
            // doguruyordu.
            Activate();

            await ShowPanelAsync(session);

            // Kayit ve veritabani kanali arka planda. Kullanici bu sirada
            // paneli kullanabiliyor.
            _ = ConnectMachineAsync(session);
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<ILogger<MainWindow>>()
                .LogError(ex, "Giriş sırasında beklenmeyen hata.");

            Hint("Giriş yapılamadı.");
            ShowProblem($"Giriş yapılamadı: {ex.Message}", canRetry: false);
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Bu makineyi sirkete baglar: once kayit, sonra veritabani kanali.
    ///
    /// Ilk giriste bu makine sirkete tanitiliyor. Sonraki acilislarda kimlik
    /// zaten yerel durum dosyasinda ve bu adim atlaniyor — kayit makineye
    /// ait, oturuma degil.
    ///
    /// Basarisizlik panelin onune gecmiyor, alttaki seritte anlatiliyor:
    /// kullanici paneli kullanmaya devam edebilir, yalnizca kendi
    /// veritabanina soru soramaz.
    /// </summary>
    private async Task ConnectMachineAsync(UserSession session)
    {
        var logger = _services.GetRequiredService<ILogger<MainWindow>>();

        try
        {
            var state = _services.GetRequiredService<BridgeState>();
            state.Load();

            if (!state.IsEnrolled && !await EnrollAsync(session))
            {
                ShowProblem(
                    "Bu bilgisayar hesabınıza bağlanamadı; panel çalışıyor ama " +
                    "kendi veritabanınıza sorgu gönderilemez.",
                    canRetry: true);
                return;
            }

            await StartAgentAsync();
            ClearProblem();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bu bilgisayar hesaba bağlanırken beklenmeyen hata.");

            ShowProblem(
                $"Bu bilgisayar hesabınıza bağlanamadı: {ex.Message}", canRetry: true);
        }
    }

    /// <summary>
    /// Kaydi birkac kez dener.
    ///
    /// Buradaki basarisizliklarin cogu gecici — bulut henuz ayakta degil, ag
    /// bir an kesildi — ve hepsinin cevabi ayni: bir sure sonra tekrar sormak.
    /// Sonsuz dongu yok: kalici bir sorunda (ornegin kayit ucu hata donduruyor)
    /// kullaniciya soylemek, sessizce denemeye devam etmekten iyi.
    /// </summary>
    private async Task<bool> EnrollAsync(UserSession session)
    {
        var enrollment = _services.GetRequiredService<BridgeEnrollment>();

        for (var attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            ShowProblem("Bu bilgisayar hesabınıza bağlanıyor…", canRetry: false, isFailure: false);

            if (await enrollment.EnrollWithTokenAsync(
                    session.AccessToken, CancellationToken.None))
            {
                return true;
            }

            if (attempt < ConnectAttempts)
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
        }

        return false;
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

        // Panel acilmiyorsa sebebi gorunsun: bos bir pencere, kullaniciya
        // hicbir sey anlatmiyor.
        Panel.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess) return;

            _services.GetRequiredService<ILogger<MainWindow>>()
                .LogError("Panel açılamadı ({Reason}): {Url}",
                    args.WebErrorStatus, _options.PanelUrl);

            ShowProblem(
                $"Panel açılamadı ({args.WebErrorStatus}). İnternet bağlantınızı " +
                "kontrol edip tekrar deneyin.",
                canRetry: true);
        };

        Panel.Source = new Uri(_options.PanelUrl);

        LoginView.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Visible;
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not { } session) return;

        RetryButton.IsEnabled = false;

        try
        {
            // Panel acilamamissa once o: kullanicinin gordugu bos pencerenin
            // sebebi bu ve tekrar denemesi kaydi beklemeden olmali.
            if (Panel.CoreWebView2 is not null) Panel.CoreWebView2.Reload();

            await ConnectMachineAsync(session);
        }
        finally
        {
            RetryButton.IsEnabled = true;
        }
    }

    private void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        LogView.Visibility = LogView.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (LogView.Visibility == Visibility.Visible) LogScroller.ScrollToEnd();
    }

    /// <summary>Giris ekranindaki tek satirlik durum metni.</summary>
    private void Hint(string text)
    {
        LoginHint.Text = text;
        LoginHint.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Alttaki serit. Hata olunca gunluk kendiliginden aciliyor: sebebi
    /// gormek icin kullanicinin once bir alani kesfetmesi gerekmemeli.
    /// </summary>
    private void ShowProblem(string text, bool canRetry, bool isFailure = true)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = text;
            StatusText.Foreground = isFailure
                ? (System.Windows.Media.Brush)FindResource("Bad")
                : (System.Windows.Media.Brush)FindResource("Ink");

            StatusBar.Visibility = Visibility.Visible;
            RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;

            if (isFailure) LogView.Visibility = Visibility.Visible;
        });
    }

    private void ClearProblem() => Dispatcher.Invoke(() =>
    {
        StatusBar.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        LogView.Visibility = Visibility.Collapsed;
    });
}

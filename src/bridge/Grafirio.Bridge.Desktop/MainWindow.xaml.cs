using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Kabugun tek penceresi: durum, kurulum onayi, baglantilar ve son olaylar.
///
/// Pencere cekirdegin durumunu OKUYOR, yonetmiyor. Kurulum akisi
/// <see cref="BridgeWorker"/>'in isi; buradaki dugmeler yalnizca zaten ekranda
/// olan seyi kolaylastiriyor (tarayiciyi ac, kodu kopyala). Kurulumu buradan
/// da baslatabilmek, ayni isi iki yerden yuruten iki yol demek olurdu.
/// </summary>
public partial class MainWindow : Window
{
    private readonly WpfBridgeDisplay _display;
    private readonly IServiceProvider _services;
    private readonly DispatcherTimer _refresh;

    private string _userCode = "";
    private string _verificationUri = "";

    public MainWindow(WpfBridgeDisplay display, IServiceProvider services)
    {
        InitializeComponent();

        _display = display;
        _services = services;

        Icon = BrandIcon.Mark();

        var options = services.GetRequiredService<IOptions<BridgeOptions>>().Value;
        SubtitleText.Text = $"Sürüm {options.Version}  ·  {options.ServerUrl}";

        display.DeviceCodeReceived += ShowPrompt;
        display.StatusChanged += ShowStatus;

        LogBuffer.Instance.LineAdded += line => Dispatcher.Invoke(() => AppendLog(line));
        LogText.Text = string.Join(Environment.NewLine, LogBuffer.Instance.Snapshot());

        // Baglanti tanimlari buluttan iniyor ve BridgeState bunu duyurmuyor.
        // Bir olay eklemek yerine kisa araliklarla okunuyor: liste birkac
        // satir ve okuma yerel bir nesneden — degisiklik duyurusu icin
        // cekirdege bir mekanizma eklemek, kazanciyla orantisiz olurdu.
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refresh.Tick += (_, _) => RefreshConnections();
        _refresh.Start();

        ShowStatus(display.Status, null);
        if (display.PendingPrompt is { } pending) ShowPrompt(pending);

        RefreshConnections();
    }

    private void ShowPrompt(DeviceCodePrompt prompt)
    {
        _userCode = prompt.UserCode;
        _verificationUri = prompt.BestUri;

        CodeText.Text = prompt.UserCode;
        VerificationUriText.Text = prompt.VerificationUri;
        SetupCard.Visibility = Visibility.Visible;

        BringToFront();
    }

    /// <summary>Pencere tepsideyse geri getirir ve one alir.</summary>
    private void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        Activate();
    }

    private void ShowStatus(BridgeStatus status, string? detail)
    {
        StatusText.Text = StatusLabel.Of(status);
        StatusDot.Fill = Dot(status);

        StatusDetail.Text = detail ?? "";
        StatusDetail.Visibility = string.IsNullOrWhiteSpace(detail)
            ? Visibility.Collapsed
            : Visibility.Visible;

        // Kurulum kartı YALNIZCA onay beklenirken duruyor. Onceki surumde onay
        // alindiktan sonra da ekranda kaliyordu: kullanici onayladigi hâlde
        // "onay bekleniyor" gorup onayin gecmedigini sanıyordu. Kart, artik
        // yapilacak bir sey kalmadiginda kayboluyor.
        if (status is not BridgeStatus.AwaitingApproval)
            SetupCard.Visibility = Visibility.Collapsed;

        // Tarayicida giris yapildiginda kullanici orada kaliyor; device
        // flow'un uygulamaya donduren bir adresi yok. Donusu uygulama kendisi
        // yapiyor: onay gelir gelmez one cikiyor.
        if (status is BridgeStatus.Registering) BringToFront();
    }

    private Brush Dot(BridgeStatus status) => status switch
    {
        BridgeStatus.Connected => (Brush)FindResource("Ok"),
        BridgeStatus.AwaitingApproval
            or BridgeStatus.Registering
            or BridgeStatus.Connecting => (Brush)FindResource("Warn"),
        BridgeStatus.EnrollmentFailed
            or BridgeStatus.Disconnected
            or BridgeStatus.Stopped => (Brush)FindResource("Bad"),
        _ => (Brush)FindResource("Muted"),
    };

    private void RefreshConnections()
    {
        var state = _services.GetRequiredService<BridgeState>();
        var connections = state.Connections;

        ConnectionsList.ItemsSource = connections;
        NoConnectionsText.Visibility = connections.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AppendLog(string line)
    {
        LogText.Text = LogText.Text.Length == 0
            ? line
            : LogText.Text + Environment.NewLine + line;

        LogScroller.ScrollToEnd();
    }

    private void SignInButton_Click(object sender, RoutedEventArgs e) =>
        WpfBridgeDisplay.OpenBrowser(_verificationUri);

    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_userCode)) return;

        try
        {
            Clipboard.SetText(_userCode);
        }
        catch (Exception ex)
        {
            // Pano baska bir uygulama tarafindan tutuluyor olabiliyor; bu
            // kurulumu engellemiyor, kod zaten ekranda.
            MessageBox.Show(
                $"Kod panoya kopyalanamadı: {ex.Message}",
                "Grafirio Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        Reveal(DesktopPaths.SettingsFile);

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e) =>
        Reveal(DesktopPaths.DataDirectory);

    /// <summary>Dosyayi ya da klasoru Gezgin'de gosterir.</summary>
    private static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                MessageBox.Show(
                    $"Bulunamadı:\n{path}",
                    "Grafirio Bridge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Açılamadı: {ex.Message}\n\n{path}",
                "Grafirio Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

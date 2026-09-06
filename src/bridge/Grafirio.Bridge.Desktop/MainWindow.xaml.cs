using System.Windows;
using Grafirio.Bridge.Desktop.Authentication;
using Grafirio.Bridge.Desktop.Cloud;
using Grafirio.Bridge.Desktop.LocalWorkspace;
using Grafirio.Bridge.Desktop.Shell;

namespace Grafirio.Bridge.Desktop;

public partial class MainWindow : Window
{
    private readonly IDesktopSessionManager _sessions;
    private readonly IDesktopBridgeController _bridge;
    private readonly LocalWorkspaceView _local;
    private readonly CloudPanel _cloud;
    private readonly ILogger<MainWindow> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _login;
    private Task? _maintenance;
    private bool _closing;

    public MainWindow(IDesktopSessionManager sessions, IDesktopBridgeController bridge,
        LocalWorkspaceView local, CloudPanel cloud, ILogger<MainWindow> logger)
    {
        InitializeComponent();
        _sessions = sessions;
        _bridge = bridge;
        _local = local;
        _cloud = cloud;
        _logger = logger;
        Icon = BrandIcon.Mark();
        LocalContent.Content = local;
        CloudContent.Content = cloud;
        _sessions.SessionChanged += OnSessionChanged;
        _bridge.Changed += OnBridgeChanged;
        _cloud.SignInRequested += OnSignInRequested;
        _cloud.SignOutRequested += OnSignOutRequested;
        _cloud.Problem += SetStatus;
    }

    public void Start() => _maintenance = MaintainSessionAsync();

    private async Task MaintainSessionAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                var session = await _sessions.GetAsync(_lifetime.Token);
                UpdateSessionDisplay();
                if (session is not null) await ConnectAsync(session);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                _logger.LogWarning("Session maintenance failed with {ErrorType}", exception.GetType().Name);
                SetStatus("Bulut oturumu doğrulanamadı. Yerel çalışma devam ediyor; bağlantı yeniden denenecek.");
            }
        } while (await timer.WaitForNextTickAsync(_lifetime.Token));
    }

    private async Task ConnectAsync(UserSession session)
    {
        try { await _bridge.ConnectAsync(session, _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogWarning("Cloud bridge connection failed with {ErrorType}", exception.GetType().Name);
            SetStatus("Bulut veri bağlantısı kurulamadı. Şirket üyeliğinizi ve interneti kontrol edin; yerel çalışma etkilenmez.");
        }
    }

    private void OnSessionChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (_closing) return;
        UpdateSessionDisplay();
        _cloud.PublishSession();
        if (_sessions.Current is null) _ = StopBridgeAsync();
    });

    private async Task StopBridgeAsync()
    {
        try { await _bridge.StopAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogWarning(exception, "Cloud bridge stop failed"); }
    }

    private void UpdateSessionDisplay()
    {
        var signedIn = _sessions.Current is not null;
        SignInButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        SignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        SessionText.Text = signedIn ? "Bulut oturumu açık" : "Yerel kullanım • Giriş Yap";
    }

    private void OnBridgeChanged(BridgeStatus status, string? detail) => Dispatcher.BeginInvoke(() =>
    {
        if (!_closing) SetStatus($"Bulut veri bağlantısı: {StatusLabel.Of(status)}. Yerel çalışma bağımsızdır.");
    });

    private void OnSignInRequested() => _ = SignInAsync();
    private void OnSignOutRequested() => _ = SignOutAsync();
    private async void SignInButton_Click(object sender, RoutedEventArgs e) => await SignInAsync();

    private async Task SignInAsync()
    {
        if (_login is not null || _closing) return;
        _login = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        SignInButton.IsEnabled = false;
        CancelLoginButton.Visibility = Visibility.Visible;
        SetStatus("Varsayılan tarayıcıda giriş bekleniyor. Yerel çalışmaya devam edebilirsiniz.");
        try
        {
            var session = await _sessions.SignInAsync(_login.Token);
            if (session is null)
            {
                SetStatus("Giriş tamamlanmadı. Giriş Yap düğmesiyle yeniden deneyebilirsiniz.");
                return;
            }
            BringToFront();
            await ShowCloudAsync();
            await ConnectAsync(session);
        }
        catch (OperationCanceledException)
        {
            if (!_closing) SetStatus("Giriş iptal edildi veya zaman aşımına uğradı. Yerel çalışma devam ediyor.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Desktop sign-in failed with {ErrorType}", exception.GetType().Name);
            if (!_closing) SetStatus("Giriş tamamlanamadı. İnternet bağlantınızı kontrol edip yeniden deneyin.");
        }
        finally
        {
            _login.Dispose();
            _login = null;
            if (!_closing)
            {
                SignInButton.IsEnabled = true;
                CancelLoginButton.Visibility = Visibility.Collapsed;
                UpdateSessionDisplay();
            }
        }
    }

    private void CancelLoginButton_Click(object sender, RoutedEventArgs e) => _login?.Cancel();
    private async void SignOutButton_Click(object sender, RoutedEventArgs e) => await SignOutAsync();

    private async Task SignOutAsync()
    {
        _login?.Cancel();
        try
        {
            await _sessions.SignOutAsync(_lifetime.Token);
            await _bridge.StopAsync(_lifetime.Token);
            ShowLocal();
            SetStatus("Masaüstü oturumu kapatıldı. Yerel veriler korunuyor. Tarayıcıdaki web oturumunuz ayrı yönetilir.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Desktop sign-out failed with {ErrorType}", exception.GetType().Name);
            SetStatus("Oturum kapatılamadı. Yeniden deneyin.");
        }
    }

    private void LocalButton_Click(object sender, RoutedEventArgs e) => ShowLocal();
    private void ShowLocal()
    {
        CloudContent.Visibility = Visibility.Collapsed;
        LocalContent.Visibility = Visibility.Visible;
    }

    private async void CloudButton_Click(object sender, RoutedEventArgs e) => await ShowCloudAsync();
    private async Task ShowCloudAsync()
    {
        try
        {
            await _local.DeactivateAsync();
            await _cloud.OpenAsync();
            if (_closing) return;
            LocalContent.Visibility = Visibility.Collapsed;
            CloudContent.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Cloud panel initialization failed");
            SetStatus("Geçiş tamamlanamadı. Yerel kaydı ve bulut bağlantısını kontrol edin; çalışma alanınız açık tutuldu.");
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _cloud.Reload();
        try
        {
            var session = await _sessions.GetAsync(_lifetime.Token);
            if (session is not null) await ConnectAsync(session);
            else SetStatus("Bulut özellikleri için Giriş Yap düğmesini kullanın.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Desktop retry failed with {ErrorType}", exception.GetType().Name);
            SetStatus("Buluta ulaşılamadı. Yerel çalışma devam ediyor.");
        }
    }

    private void SetStatus(string message)
    {
        if (!_closing) StatusText.Text = message;
    }

    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public async Task<bool> PrepareToExitAsync()
    {
        try
        {
            await _local.DeactivateAsync();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Pending local work could not be saved before exit");
            ShowLocal();
            BringToFront();
            SetStatus("Son değişiklikler kaydedilemedi. Çıkış iptal edildi; kaydı kontrol edip yeniden deneyin.");
            return false;
        }
    }

    public async Task StopAsync()
    {
        _closing = true;
        _sessions.SessionChanged -= OnSessionChanged;
        _bridge.Changed -= OnBridgeChanged;
        _lifetime.Cancel();
        _local.Dispose();
        _cloud.Dispose();
        if (_maintenance is not null)
        {
            try { await _maintenance; }
            catch (OperationCanceledException) { /* Shutdown cancels the periodic timer. */ }
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _bridge.StopAsync(deadline.Token);
    }
}
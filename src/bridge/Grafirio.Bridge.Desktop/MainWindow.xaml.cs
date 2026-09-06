using System.Windows;
using Grafirio.Bridge.Desktop.Authentication;
using Grafirio.Bridge.Desktop.Cloud;
using Grafirio.Bridge.Desktop.Shell;

namespace Grafirio.Bridge.Desktop;

public partial class MainWindow : Window
{
    private readonly IDesktopSessionManager _sessions;
    private readonly IDesktopBridgeController _bridge;
    private readonly CloudPanel _cloud;
    private readonly ILogger<MainWindow> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _login;
    private Task? _maintenance;
    private string? _problem;
    private bool _closing;

    public MainWindow(IDesktopSessionManager sessions, IDesktopBridgeController bridge,
        CloudPanel cloud, ILogger<MainWindow> logger)
    {
        InitializeComponent();
        _sessions = sessions;
        _bridge = bridge;
        _cloud = cloud;
        _logger = logger;
        Icon = BrandIcon.Mark();
        CloudContent.Content = cloud;
        _sessions.SessionChanged += OnSessionChanged;
        _bridge.Changed += OnBridgeChanged;
        _cloud.SignInRequested += OnSignInRequested;
        _cloud.SignOutRequested += OnSignOutRequested;
        _cloud.Problem += SetStatus;
    }

    public void Start()
    {
        UpdateSessionDisplay();
        _ = ShowCloudAsync();
        _maintenance = MaintainSessionAsync();
    }

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
                SetStatus("Oturum doğrulanamadı. İnternet bağlantınızı kontrol edin; yeniden denenecek.");
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
            SetStatus("Veri bağlantısı kurulamadı. Şirket üyeliğinizi ve internet bağlantınızı kontrol edip yeniden deneyin.");
        }
    }

    private void OnSessionChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (_closing) return;
        UpdateSessionDisplay();
        if (_sessions.Current is { } session) _ = ConnectAsync(session);
        else _ = StopBridgeAsync();
        _cloud.PublishSession();
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
        NoticeBar.Visibility = !signedIn || _problem is not null || _login is not null
            ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = _problem is not null && _login is null ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _problem ?? (signedIn ? string.Empty : "Grafirio'yu kullanmak için giriş yapın.");
    }

    private void OnBridgeChanged(BridgeStatus status, string? detail) => Dispatcher.BeginInvoke(() =>
    {
        if (_closing) return;
        if (status == BridgeStatus.Connected) SetStatus(null);
        else if (status is BridgeStatus.Disconnected or BridgeStatus.EnrollmentFailed
            or BridgeStatus.AwaitingApproval or BridgeStatus.AwaitingEnrollment)
            SetStatus(detail ?? "Veri bağlantısı kurulamadı. Yeniden deneyin.");
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
        SetStatus("Varsayılan tarayıcıda giriş bekleniyor.");
        try
        {
            var session = await _sessions.SignInAsync(_login.Token);
            if (session is null)
            {
                SetStatus("Giriş tamamlanmadı. Giriş Yap düğmesiyle yeniden deneyebilirsiniz.");
                return;
            }
            BringToFront();
            SetStatus(null);
            var connection = ConnectAsync(session);
            await ShowCloudAsync();
            await connection;
        }
        catch (OperationCanceledException)
        {
            if (!_closing) SetStatus("Giriş iptal edildi veya zaman aşımına uğradı. Yeniden deneyebilirsiniz.");
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

    private async Task SignOutAsync()
    {
        _login?.Cancel();
        try
        {
            await _sessions.SignOutAsync(_lifetime.Token);
            await _bridge.StopAsync(_lifetime.Token);
            SetStatus(null);
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Desktop sign-out failed with {ErrorType}", exception.GetType().Name);
            SetStatus("Oturum kapatılamadı. Yeniden deneyin.");
        }
    }

    private async Task ShowCloudAsync()
    {
        try
        {
            await _cloud.OpenAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Cloud panel initialization failed");
            SetStatus("Grafirio açılamadı. İnternet bağlantısını ve WebView2 kurulumunu kontrol edip yeniden deneyin.");
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(null);
        await ShowCloudAsync();
        _cloud.Reload();
        try
        {
            var session = await _sessions.GetAsync(_lifetime.Token);
            if (session is not null) await ConnectAsync(session);
            else SetStatus("Grafirio'yu kullanmak için Giriş Yap düğmesini kullanın.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Desktop retry failed with {ErrorType}", exception.GetType().Name);
            SetStatus("Grafirio'ya ulaşılamadı. İnternet bağlantınızı kontrol edip yeniden deneyin.");
        }
    }

    private void SetStatus(string? message)
    {
        if (_closing) return;
        _problem = message;
        UpdateSessionDisplay();
    }

    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public async Task StopAsync()
    {
        _closing = true;
        _sessions.SessionChanged -= OnSessionChanged;
        _bridge.Changed -= OnBridgeChanged;
        _lifetime.Cancel();
        _cloud.SignInRequested -= OnSignInRequested;
        _cloud.SignOutRequested -= OnSignOutRequested;
        _cloud.Problem -= SetStatus;
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
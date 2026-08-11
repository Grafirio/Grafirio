using System.Windows;
using System.Windows.Forms;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Tepsi simgesi ve menusu.
///
/// Pencere kapatildiginda uygulama kapanmiyor, tepsiye iniyor: bridge'in isi
/// arka planda acik kalmak. Kullanicinin pencereyi kapatmasi "artik
/// baglanma" demek degil — oyle olsaydi, panelde bridge'in neden cevrimdisi
/// oldugunu kimse anlamazdi.
///
/// Cikis yalnizca tepsi menusunden: kasitli bir eylem olmasi gerekiyor.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Window _window;

    private bool _warnedAboutHiding;

    public TrayIcon(Window window, Action exit)
    {
        _window = window;

        _icon = new NotifyIcon
        {
            Icon = BrandIcon.Create(connected: false),
            Text = "Grafirio Bridge",
            Visible = true,
        };

        _icon.DoubleClick += (_, _) => Show();

        _icon.ContextMenuStrip = new ContextMenuStrip();
        _icon.ContextMenuStrip.Items.Add("Göster", null, (_, _) => Show());
        _icon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _icon.ContextMenuStrip.Items.Add("Çıkış", null, (_, _) => exit());

        window.Closing += (_, e) =>
        {
            e.Cancel = true;
            window.Hide();
            WarnOnce();
        };
    }

    public void Update(BridgeStatus status)
    {
        var old = _icon.Icon;

        _icon.Icon = BrandIcon.Create(status == BridgeStatus.Connected);
        old?.Dispose();

        _icon.Text = $"Grafirio Bridge — {StatusLabel.Of(status)}";
    }

    private void Show()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
    }

    /// <summary>
    /// Ilk kapatista bir kez soyleniyor. Her seferinde balon gostermek, bir
    /// sure sonra okunmayan bir seye donuserdi.
    /// </summary>
    private void WarnOnce()
    {
        if (_warnedAboutHiding) return;
        _warnedAboutHiding = true;

        _icon.ShowBalloonTip(
            4000,
            "Grafirio Bridge arka planda çalışıyor",
            "Bağlantı açık kalır. Çıkmak için tepsideki simgeye sağ tıklayın.",
            ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}

/// <summary>Durumun kullaniciya gorunen hâli.</summary>
public static class StatusLabel
{
    public static string Of(BridgeStatus status) => status switch
    {
        BridgeStatus.AwaitingEnrollment => "Kurulum bekleniyor",
        BridgeStatus.AwaitingApproval => "Onay bekleniyor",
        BridgeStatus.Registering => "Onay alındı, kaydolunuyor",
        BridgeStatus.EnrollmentFailed => "Kayıt yapılamadı",
        BridgeStatus.Connecting => "Bağlanılıyor",
        BridgeStatus.Connected => "Bağlı",
        BridgeStatus.Disconnected => "Bağlantı koptu",
        BridgeStatus.Stopped => "Durdu",
        _ => "Bilinmiyor",
    };
}

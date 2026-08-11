using System.Windows;
using System.Windows.Threading;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Pencereli kabugun <see cref="IBridgeDisplay"/> karsiligi.
///
/// Cekirdek bu cagrilari kendi is parcaciginda yapiyor; WPF ise arayuze
/// yalnizca kendi parcacigindan dokunulmasina izin veriyor. Gecis burada, tek
/// yerde yapiliyor — pencerelerin icine dagilsaydi, unutulan bir yer sahada
/// "arayuz donuyor" olarak gorunurdu.
/// </summary>
public class WpfBridgeDisplay(Dispatcher dispatcher) : IBridgeDisplay
{
    /// <summary>Kuran kisiye gosterilecek kod geldi.</summary>
    public event Action<DeviceCodePrompt>? DeviceCodeReceived;

    /// <summary>Durum degisti.</summary>
    public event Action<BridgeStatus, string?>? StatusChanged;

    /// <summary>Son bilinen durum: pencere sonradan acildiginda okunuyor.</summary>
    public BridgeStatus Status { get; private set; } = BridgeStatus.AwaitingEnrollment;

    /// <summary>Onay hâlâ bekliyorsa duran kod; pencere kapatilip acilinca kayboldu sanilmasin.</summary>
    public DeviceCodePrompt? PendingPrompt { get; private set; }

    public void ShowDeviceCode(DeviceCodePrompt prompt)
    {
        PendingPrompt = prompt;

        // Tarayici KENDILIGINDEN acilmiyor: kullanici "Giriş Yap"a basinca
        // aciliyor. Kendiliginden acmak, uygulamayi ilk kez calistiran birinin
        // onune sormadan bir tarayici penceresi koymak olurdu — ustelik kod
        // suresi dolup yenilendiginde bunu tekrar tekrar yapardi.
        dispatcher.Invoke(() => DeviceCodeReceived?.Invoke(prompt));
    }

    public void ShowStatus(BridgeStatus status, string? detail = null)
    {
        Status = status;

        // Kurulum bittiginde kod artik gecersiz; ekranda birakmak "hâlâ bir
        // sey yapmam gerekiyor" izlenimi verirdi.
        if (status is BridgeStatus.Connecting or BridgeStatus.Connected)
            PendingPrompt = null;

        dispatcher.Invoke(() => StatusChanged?.Invoke(status, detail));
    }

    /// <summary>
    /// Varsayilan tarayicida acar. Acilamamasi kurulumu bozmuyor — adres ve
    /// kod ekranda duruyor, kullanici elle girebilir; bu yuzden hata
    /// yutuluyor ama sessizce degil, pencerede zaten gorunur durumda.
    /// </summary>
    public static void OpenBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Tarayıcı açılamadı: {ex.Message}\n\nAdresi elle açabilirsiniz:\n{url}",
                "Grafirio Bridge",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}

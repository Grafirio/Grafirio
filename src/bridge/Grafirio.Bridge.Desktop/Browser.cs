using System.Windows;

namespace Grafirio.Bridge.Desktop;

/// <summary>Varsayilan tarayiciyi acar.</summary>
public static class Browser
{
    /// <summary>
    /// Acilamamasi akisi bozmuyor ama sessiz de kalmamali: kullanici
    /// dugmeye bastiginda hicbir sey olmazsa uygulamanin donduğunu sanar.
    /// </summary>
    public static void Open(string url)
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
                "Grafirio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}

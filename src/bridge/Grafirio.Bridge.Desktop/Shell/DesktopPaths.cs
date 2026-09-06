using System.IO;

namespace Grafirio.Bridge.Desktop.Shell;

public static class DesktopPaths
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Grafirio", "Desktop");
}
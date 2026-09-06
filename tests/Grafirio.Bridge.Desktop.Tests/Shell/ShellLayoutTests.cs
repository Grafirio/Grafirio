using System.Xml.Linq;
using Grafirio.Bridge.Desktop.Shell;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

public sealed class ShellLayoutTests
{
    [Fact]
    public void WebsiteIsTheOnlyFullSizeContentAndNoticeIsHiddenByDefault()
    {
        using var stream = typeof(ShellLayoutTests).Assembly.GetManifestResourceStream("Shell.MainWindow.xaml");
        Assert.NotNull(stream);
        var document = XDocument.Load(stream);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var content = Assert.Single(document.Descendants(presentation + "ContentControl"));
        Assert.Equal("CloudContent", content.Attribute(xaml + "Name")?.Value);
        Assert.Null(content.Attribute("Visibility"));
        Assert.Null(content.Attribute("Grid.Row"));
        var rows = document.Descendants(presentation + "RowDefinition").ToArray();
        Assert.Equal(new[] { "*", "Auto" }, rows.Select(row => row.Attribute("Height")?.Value));
        var notice = Assert.Single(document.Descendants(presentation + "Border"));
        Assert.Equal("NoticeBar", notice.Attribute(xaml + "Name")?.Value);
        Assert.Equal("Collapsed", notice.Attribute("Visibility")?.Value);
        Assert.Equal(new[] { "Giriş Yap", "İptal", "Yeniden dene" },
            notice.Descendants(presentation + "Button").Select(button => button.Attribute("Content")?.Value));
    }

    [Fact]
    public void DesktopAssemblyHasNoLocalWorkspaceResourcesOrTypes()
    {
        var assembly = typeof(CloudPanel).Assembly;
        Assert.DoesNotContain(assembly.GetManifestResourceNames(), name => name.Contains("LocalWorkspace"));
        Assert.DoesNotContain(assembly.GetTypes(), type => type.Namespace?.Contains("LocalWorkspace") == true);
    }
}
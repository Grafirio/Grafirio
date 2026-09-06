using Grafirio.Bridge.Desktop.Shell;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

public sealed class PanelOriginTests
{
    [Theory]
    [InlineData("https://admin.grafirio.com/page", true)]
    [InlineData("https://admin.grafirio.com:443/page", true)]
    [InlineData("https://admin.grafirio.com.evil.test/", false)]
    [InlineData("https://evil.test/", false)]
    [InlineData("https://admin.grafirio.com:444/", false)]
    [InlineData("https://user@admin.grafirio.com/", false)]
    [InlineData("http://admin.grafirio.com/", false)]
    [InlineData("file:///C:/page.html", false)]
    public void OnlyConfiguredOriginIsTrusted(string candidate, bool expected) =>
        Assert.Equal(expected, new PanelOrigin("https://admin.grafirio.com").Contains(candidate));

    [Theory]
    [InlineData("http://admin.grafirio.com")]
    [InlineData("file:///C:/page.html")]
    [InlineData("https://user@admin.grafirio.com")]
    public void RejectsUnsafePanelConfiguration(string address) =>
        Assert.Throws<ArgumentException>(() => new PanelOrigin(address));
}
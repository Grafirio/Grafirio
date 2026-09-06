using System.IO;
using Grafirio.Bridge.Desktop.Cloud;

namespace Grafirio.Bridge.Desktop.Tests.Cloud;

public sealed class DesktopBridgeOptionsTests
{
    [Theory]
    [InlineData("http://cloud.example", "https://identity.example/realms/grafirio")]
    [InlineData("https://cloud.example", "http://identity.example/realms/grafirio")]
    [InlineData("https://cloud.example?secret=value", "https://identity.example/realms/grafirio")]
    [InlineData("https://cloud.example", "https://secret@identity.example/realms/grafirio")]
    public void InvalidEndpointsAreRejectedBeforeCreatingStateDirectory(string server, string identity)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var source = new BridgeOptions { ServerUrl = server, IdentityUrl = identity };

        var failure = Assert.Throws<InvalidOperationException>(() => DesktopBridgeOptions.Create(source, directory));
        Assert.DoesNotContain("secret", failure.ToString());
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData("https://cloud.example", "https://identity.example/realms/grafirio")]
    [InlineData("http://127.0.0.1:5221", "http://localhost:8080/realms/grafirio")]
    [InlineData("http://[::1]:5221", "http://[::1]:8080/realms/grafirio")]
    public void SecureAndLoopbackConfigurationIsCopiedWithoutMutatingSource(string server, string identity)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var source = new BridgeOptions
        {
            ServerUrl = server, IdentityUrl = identity, InstallerClientId = "installer",
            Name = "Desktop", PanelUrl = "https://panel.example", ConfigurationPath = "configuration.json",
            Version = "2.0", StatePath = "original-state", AuditLogPath = "original-audit"
        };

        var copy = DesktopBridgeOptions.Create(source, directory);

        Assert.Equal(server, copy.ServerUrl);
        Assert.Equal(identity, copy.IdentityUrl);
        Assert.Equal(source.InstallerClientId, copy.InstallerClientId);
        Assert.Equal(source.Name, copy.Name);
        Assert.Equal(source.PanelUrl, copy.PanelUrl);
        Assert.Equal(source.ConfigurationPath, copy.ConfigurationPath);
        Assert.Equal(source.Version, copy.Version);
        Assert.Equal(Path.Combine(directory, "state.dat"), copy.StatePath);
        Assert.Equal(Path.Combine(directory, "audit.tsv"), copy.AuditLogPath);
        Assert.Equal("original-state", source.StatePath);
        Assert.Equal("original-audit", source.AuditLogPath);
        Assert.False(Directory.Exists(directory));
    }
}
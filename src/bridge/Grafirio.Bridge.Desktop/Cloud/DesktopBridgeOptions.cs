using System.IO;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Grafirio.Bridge.Desktop.Tests")]

namespace Grafirio.Bridge.Desktop.Cloud;

internal static class DesktopBridgeOptions
{
    private const string StateFileName = "state.dat";
    private const string AuditFileName = "audit.tsv";

    public static BridgeOptions Create(BridgeOptions source, string directory)
    {
        BridgeEndpointSecurity.GetEnrollmentEndpoint(source);
        return new BridgeOptions
        {
            ServerUrl = source.ServerUrl,
            IdentityUrl = source.IdentityUrl,
            InstallerClientId = source.InstallerClientId,
            Name = source.Name,
            PanelUrl = source.PanelUrl,
            StatePath = Path.GetFullPath(Path.Combine(directory, StateFileName)),
            AuditLogPath = Path.GetFullPath(Path.Combine(directory, AuditFileName)),
            ConfigurationPath = source.ConfigurationPath,
            Version = source.Version
        };
    }
}
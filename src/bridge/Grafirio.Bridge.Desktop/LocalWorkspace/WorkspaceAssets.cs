namespace Grafirio.Bridge.Desktop.LocalWorkspace;

public static class WorkspaceAssets
{
    private const string ResourcePrefix = "Grafirio.Bridge.Desktop.LocalWorkspace.Web.";
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.Ordinal)
    {
        ["index.html"] = "text/html; charset=utf-8",
        ["workspace.css"] = "text/css; charset=utf-8",
        ["app.js"] = "text/javascript; charset=utf-8",
        ["channel.js"] = "text/javascript; charset=utf-8",
        ["connections.js"] = "text/javascript; charset=utf-8",
        ["results.js"] = "text/javascript; charset=utf-8"
    };

    public static (string ResourceName, string ContentType)? Find(string url)
    {
        foreach (var (name, contentType) in ContentTypes)
            if (string.Equals(url, Messaging.WorkspaceProtocol.Origin + "/" + name, StringComparison.Ordinal))
                return (ResourcePrefix + name, contentType);
        return null;
    }
}
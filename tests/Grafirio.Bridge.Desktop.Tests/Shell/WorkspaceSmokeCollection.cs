namespace Grafirio.Bridge.Desktop.Tests.Shell;

// WebView2 profile overrides are process-wide, so other test collections must not overlap.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkspaceSmokeCollection
{
    public const string Name = "Isolated Windows WebView2 smoke";
}
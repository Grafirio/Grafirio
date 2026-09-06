namespace Grafirio.Bridge.Desktop.LocalWorkspace.Models;

public sealed record LocalQueryResult(
    IReadOnlyList<string> Columns, IReadOnlyList<string?[]> Rows, bool Truncated, long ElapsedMilliseconds);
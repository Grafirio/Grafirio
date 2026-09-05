using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.Bridge;

/// <summary>Extracts physical objects from the validated AST, never CTE or derived aliases.</summary>
public static class SqlTableScanner
{
    public static IEnumerable<string> ReferencedTables(string sql) =>
        SqlPolicy.Inspect(sql).Tables.Select(table => table.CanonicalName);
}
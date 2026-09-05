using System.Text.Json;
using Grafirio.QueryPolicy;
using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

/// <summary>Exact physical table identities shared by selection and execution boundaries.</summary>
public static class QueryTableScope
{
    public const string BaseTablesSql = """
        SELECT s.name AS SchemaName, t.name AS TableName
        FROM sys.tables AS t
        INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0 AND t.is_external = 0
        """;

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? tables)
    {
        if (tables is null)
            throw new QueryPolicyException("At least one selected table is required.", true);

        var identities = new HashSet<SqlTableIdentity>();
        var result = new List<string>();
        foreach (var name in tables)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new QueryPolicyException("Table names cannot be empty.", true);

            var identity = SqlPolicy.ParseTableIdentity(name.Trim());
            if (identity.Schema!.Equals("sys", StringComparison.OrdinalIgnoreCase) ||
                identity.Schema.Equals("INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase))
                throw new QueryPolicyException("Only customer base tables can be selected.", true);

            if (identities.Add(identity)) result.Add(name.Trim());
        }

        if (result.Count == 0)
            throw new QueryPolicyException("At least one selected table is required.", true);

        return result;
    }

    public static IReadOnlyList<string> RequireSelected(
        IEnumerable<string>? requestedTables, IEnumerable<string>? selectedTables)
    {
        var requested = Normalize(requestedTables);
        var selected = Normalize(selectedTables).Select(SqlPolicy.ParseTableIdentity).ToHashSet();
        if (requested.Any(table => !selected.Contains(SqlPolicy.ParseTableIdentity(table))))
            throw new QueryPolicyException("Requested tables are outside the current selection.", true);
        return requested;
    }

    public static IReadOnlyList<string> RequireCurrentConfig(
        string tablesJson, IEnumerable<string>? selectedTables)
    {
        IReadOnlyList<string> configured;
        try
        {
            configured = Normalize(JsonSerializer.Deserialize<List<string>>(tablesJson));
        }
        catch (JsonException)
        {
            throw new QueryPolicyException("The configured table scope is invalid.", true);
        }

        var configuredIdentities = configured.Select(SqlPolicy.ParseTableIdentity).ToHashSet();
        var selectedIdentities = Normalize(selectedTables).Select(SqlPolicy.ParseTableIdentity).ToHashSet();
        if (!configuredIdentities.SetEquals(selectedIdentities))
            throw new QueryPolicyException("The configured table scope is stale.", true);
        return configured;
    }

    public static void RequireBaseTables(
        IEnumerable<string> requestedTables, IEnumerable<SqlTableIdentity> baseTables)
    {
        var actual = baseTables.ToHashSet();
        if (Normalize(requestedTables).Any(table => !actual.Contains(SqlPolicy.ParseTableIdentity(table))))
            throw new QueryPolicyException("Only existing local user base tables can be selected.", true);
    }
}
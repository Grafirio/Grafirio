using System.Text.Json;
using Grafirio.QueryPolicy;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

public static class AgentQueryTableScope
{
    public static void Validate(string parametersJson, string dictionaryJson, IReadOnlyList<string> selected)
    {
        try
        {
            using var dictionary = JsonDocument.Parse(dictionaryJson);
            var tables = dictionary.RootElement.GetProperty("tables").EnumerateArray()
                .Select(table => table.ValueKind == JsonValueKind.String
                    ? table.GetString()! : table.GetProperty("name").GetString()!).ToList();
            QueryTableScope.RequireCurrentConfig(JsonSerializer.Serialize(tables), selected);

            using var parameters = JsonDocument.Parse(parametersJson);
            ValidateBranch(parameters.RootElement, selected, "target_table", allowEmptyTarget: true);
            if (parameters.RootElement.TryGetProperty("relationship_proposals", out var proposals))
                foreach (var proposal in proposals.EnumerateArray())
                    QueryTableScope.RequireSelected(
                        [proposal.GetProperty("fromTable").GetString()!, proposal.GetProperty("toTable").GetString()!], selected);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new QueryPolicyException("Analysis parameters or dictionary table scope is invalid.", true);
        }
    }

    private static void ValidateBranch(JsonElement branch, IReadOnlyList<string> selected,
        string tableProperty, bool allowEmptyTarget = false)
    {
        if (branch.TryGetProperty(tableProperty, out var table) && table.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(table.GetString()))
            QueryTableScope.RequireSelected([table.GetString()!], selected);
        else if (!allowEmptyTarget)
            throw new QueryPolicyException("An analysis branch must name a selected table.", true);

        if (branch.TryGetProperty("joins", out var joins) && joins.ValueKind != JsonValueKind.Null)
            foreach (var join in joins.EnumerateArray()) ValidateBranch(join, selected, "table");

        var aliases = new HashSet<string>(StringComparer.Ordinal) { "base" };
        if (joins.ValueKind == JsonValueKind.Array)
            foreach (var join in joins.EnumerateArray())
                if (join.TryGetProperty("as", out var alias) && alias.ValueKind == JsonValueKind.String)
                    aliases.Add(alias.GetString()!);
        ValidateColumns(branch, selected, aliases);

        if (branch.TryGetProperty("union", out var union) && union.ValueKind != JsonValueKind.Null)
        {
            var branches = union.ValueKind == JsonValueKind.Array ? union : union.GetProperty("with");
            foreach (var child in branches.EnumerateArray()) ValidateBranch(child, selected, "table");
        }
    }

    private static void ValidateColumns(JsonElement branch, IReadOnlyList<string> selected, HashSet<string> aliases)
    {
        foreach (var name in new[] { "target_column", "sort_by", "column" })
            if (branch.TryGetProperty(name, out var column) && column.ValueKind == JsonValueKind.String)
                ValidateColumn(column.GetString()!, selected, aliases);
        foreach (var name in new[] { "group_by", "feature_columns" })
            if (branch.TryGetProperty(name, out var columns) && columns.ValueKind != JsonValueKind.Null)
                foreach (var column in columns.EnumerateArray()) ValidateColumn(column.GetString()!, selected, aliases);
        foreach (var name in new[] { "filters", "filter" })
            if (branch.TryGetProperty(name, out var filters) && filters.ValueKind != JsonValueKind.Null)
                foreach (var filter in filters.EnumerateObject()) ValidateColumn(filter.Name, selected, aliases);
        if (branch.TryGetProperty("preAggregate", out var aggregate) && aggregate.ValueKind == JsonValueKind.Object)
            ValidateColumns(aggregate, selected, aliases);
        if (branch.TryGetProperty("having", out var having) && having.ValueKind != JsonValueKind.Null)
        {
            if (having.ValueKind == JsonValueKind.Array)
                foreach (var condition in having.EnumerateArray()) ValidateColumns(condition, selected, aliases);
            else ValidateColumns(having, selected, aliases);
        }
    }

    private static void ValidateColumn(string reference, IReadOnlyList<string> selected, HashSet<string> aliases)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference == "*") return;
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader($"SELECT {reference}"), out var errors);
        if (errors.Count != 0 || fragment is not TSqlScript { Batches.Count: 1 } script ||
            script.Batches[0].Statements.Count != 1 ||
            script.Batches[0].Statements[0] is not SelectStatement
            { QueryExpression: QuerySpecification { SelectElements.Count: 1 } specification } ||
            specification.SelectElements[0] is not SelectScalarExpression
            { ColumnName: null, Expression: ColumnReferenceExpression { MultiPartIdentifier: not null } column } ||
            specification.FromClause is not null || specification.WhereClause is not null ||
            column.FragmentLength != reference.Trim().Length)
            throw new QueryPolicyException("Analysis column references must be identifiers, not SQL expressions.", true);

        var parts = column.MultiPartIdentifier.Identifiers;
        var identities = selected.Select(Grafirio.QueryPolicy.QueryPolicy.ParseTableIdentity).ToList();
        var allowed = parts.Count switch
        {
            1 => true,
            2 => aliases.Contains(parts[0].Value) || identities.Any(table => table.Name == parts[0].Value),
            3 => identities.Contains(new SqlTableIdentity(parts[0].Value, parts[1].Value)),
            _ => false
        };
        if (!allowed) throw new QueryPolicyException("A qualified column refers to a table outside the selection.", true);
    }
}
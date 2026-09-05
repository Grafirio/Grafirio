namespace Grafirio.QueryPolicy;

/// <summary>Shared parser, independently invoked by each execution boundary.</summary>
public static class QueryPolicy
{
    private static readonly HashSet<SqlTableIdentity> MetadataTables =
    [
        new("sys", "tables"), new("sys", "columns"), new("sys", "schemas"),
        new("sys", "objects"), new("sys", "partitions"), new("sys", "indexes"),
        new("sys", "index_columns"), new("sys", "foreign_keys"), new("sys", "foreign_key_columns"),
        new("INFORMATION_SCHEMA", "TABLES"), new("INFORMATION_SCHEMA", "COLUMNS"),
        new("INFORMATION_SCHEMA", "TABLE_CONSTRAINTS"), new("INFORMATION_SCHEMA", "KEY_COLUMN_USAGE")
    ];

    /// <summary>
    /// Validates a single SELECT and its physical tables. Null and empty allowlists deny customer
    /// tables. Metadata access is opt-in and limited to exact profiling catalog identities.
    /// Customer SQL and allowlist entries must use two-part schema-qualified names.
    /// This is not a database permission guarantee; execution also requires the principal and
    /// physical-object checks in <see cref="ReadOnlyPrincipalGuard"/>.
    /// </summary>
    public static QueryValidationResult Validate(
        string sql, IEnumerable<string>? allowedTables = null, bool allowMetadata = false)
    {
        var result = Inspect(sql, allowMetadata);
        var allowed = (allowedTables ?? []).Select(ParseTableIdentity).ToHashSet();
        foreach (var table in result.Tables)
        {
            if (table.Schema is null)
                throw new QueryPolicyException("Physical table references must be schema-qualified.", true);

            if (IsMetadata(table))
            {
                if (allowMetadata) continue;
                throw new QueryPolicyException("Metadata access is not enabled for this query.", true);
            }

            if (table.Schema.Equals("sys", StringComparison.OrdinalIgnoreCase) ||
                table.Schema.Equals("INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase) ||
                !allowed.Contains(table))
                throw new QueryPolicyException($"Table '{table.CanonicalName}' is not allowed.", true);
        }

        return result;
    }

    /// <summary>Structural validation only. Never use this instead of Validate at execution boundaries.</summary>
    public static QueryValidationResult Inspect(string sql, bool allowMetadata = false)
    {
        var visitor = new SelectPolicyVisitor(allowMetadata);
        SqlParser.Parse(sql).Accept(visitor);
        return new QueryValidationResult(visitor.Tables.Distinct().ToArray());
    }

    public static SqlTableIdentity ParseTableIdentity(string name)
    {
        var select = SqlParser.Parse($"SELECT 1 FROM {name}");
        if (select.QueryExpression is not Microsoft.SqlServer.TransactSql.ScriptDom.QuerySpecification
            { FromClause.TableReferences.Count: 1 } query ||
            query.FromClause.TableReferences[0] is not Microsoft.SqlServer.TransactSql.ScriptDom.NamedTableReference
            { Alias: null, TableHints.Count: 0, TableSampleClause: null, TemporalClause: null } table ||
            query.WhereClause is not null || query.GroupByClause is not null ||
            query.HavingClause is not null || query.OrderByClause is not null ||
            query.OffsetClause is not null || query.ForClause is not null ||
            select.OptimizerHints.Count != 0 || select.Into is not null ||
            table.SchemaObject.Identifiers.Count != 2 ||
            table.FragmentLength != name.Trim().Length)
            throw new QueryPolicyException("Allowlist entries must be exact two-part SQL identifiers.", true);

        var identity = new SqlTableIdentity(table.SchemaObject.SchemaIdentifier.Value,
            table.SchemaObject.BaseIdentifier.Value);
        if (identity.Name.StartsWith('#') || identity.Schema!.Length == 0)
            throw new QueryPolicyException("Temporary or incomplete table identities are not allowed.", true);
        return identity;
    }

    public static bool IsMetadata(SqlTableIdentity table) => MetadataTables.Contains(table);
}
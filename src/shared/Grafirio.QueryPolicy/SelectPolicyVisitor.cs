using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Grafirio.QueryPolicy;

internal sealed class SelectPolicyVisitor(bool allowMetadata) : TSqlFragmentVisitor
{
    // An explicit node allowlist keeps new parser features fail-closed, including special
    // functions that do not appear as FunctionCall nodes (for example NEXT VALUE FOR).
    private static readonly HashSet<string> AllowedNodes = new(StringComparer.Ordinal)
    {
        "SelectStatement", "QuerySpecification", "BinaryQueryExpression", "QueryParenthesisExpression",
        "WithCtesAndXmlNamespaces", "CommonTableExpression", "SelectScalarExpression", "SelectStarExpression",
        "Identifier", "IdentifierOrValueExpression", "MultiPartIdentifier", "SchemaObjectName",
        "FromClause", "NamedTableReference", "QueryDerivedTable", "QualifiedJoin", "UnqualifiedJoin",
        "JoinParenthesisTableReference", "WhereClause", "GroupByClause", "ExpressionGroupingSpecification",
        "HavingClause", "OrderByClause", "ExpressionWithSortOrder", "OffsetClause", "TopRowFilter",
        "ColumnReferenceExpression", "IntegerLiteral", "NumericLiteral", "RealLiteral", "StringLiteral",
        "NullLiteral", "BinaryLiteral", "VariableReference", "ParenthesisExpression", "UnaryExpression",
        "BinaryExpression", "BooleanComparisonExpression", "BooleanBinaryExpression", "BooleanNotExpression",
        "BooleanParenthesisExpression", "BooleanIsNullExpression", "BooleanTernaryExpression", "InPredicate",
        "LikePredicate", "ExistsPredicate", "SubqueryComparisonPredicate", "ScalarSubquery",
        "SearchedCaseExpression", "SearchedWhenClause", "SimpleCaseExpression", "SimpleWhenClause",
        "CastCall", "ConvertCall", "TryCastCall", "TryConvertCall", "SqlDataTypeReference",
        "CoalesceExpression", "NullIfExpression", "IIfCall", "FunctionCall", "OverClause",
        "WindowFrameClause", "WindowDelimiter", "WithinGroupClause", "TableSampleClause"
    };

    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP",
        "ABS", "CEILING", "FLOOR", "ROUND", "POWER", "SQRT", "SIGN", "LOG", "LOG10", "EXP",
        "ISNULL", "LEN", "DATALENGTH", "LOWER", "UPPER", "LTRIM", "RTRIM", "TRIM", "LEFT", "RIGHT",
        "SUBSTRING", "REPLACE", "CONCAT", "CONCAT_WS", "CHARINDEX", "PATINDEX", "STUFF", "REVERSE",
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATEPART", "DATENAME", "DAY", "MONTH", "YEAR",
        "EOMONTH", "DATEFROMPARTS", "DATETIMEFROMPARTS", "GETDATE", "GETUTCDATE", "SYSDATETIME",
        "SYSUTCDATETIME", "SYSDATETIMEOFFSET", "ISDATE", "ISNUMERIC", "ROW_NUMBER", "RANK",
        "DENSE_RANK", "NTILE", "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE", "STRING_AGG"
    };

    private readonly HashSet<string> _visibleCtes = new(StringComparer.Ordinal);
    internal List<SqlTableIdentity> Tables { get; } = [];

    public override void Visit(TSqlFragment node)
    {
        if (!AllowedNodes.Contains(node.GetType().Name))
            throw new QueryPolicyException($"Unsupported SQL construct: {node.GetType().Name}.");
        base.Visit(node);
    }

    public override void ExplicitVisit(SelectStatement node)
    {
        if (node.Into is not null || node.On is not null || node.ComputeClauses.Count != 0 ||
            node.OptimizerHints.Count != 0)
            throw new QueryPolicyException("SELECT INTO, compute clauses and query hints are forbidden.");
        Visit((TSqlFragment)node);
        // ScriptDom's default traversal visits the query before the WITH clause.
        // SQL name resolution requires the opposite order.
        node.WithCtesAndXmlNamespaces?.Accept(this);
        node.QueryExpression.Accept(this);
    }

    public override void ExplicitVisit(CommonTableExpression node)
    {
        // A CTE sees itself and preceding CTEs, not following definitions. Registering all
        // aliases up front could hide a physical table referenced in an earlier definition.
        if (!_visibleCtes.Add(node.ExpressionName.Value))
            throw new QueryPolicyException("Duplicate CTE names are not allowed.");
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(NamedTableReference node)
    {
        if (node.TableHints.Count != 0 || node.TemporalClause is not null || node.ForPath)
            throw new QueryPolicyException("Table hints and temporal or graph references are forbidden.");

        var identifiers = node.SchemaObject.Identifiers;
        if (identifiers.Count is < 1 or > 2 || identifiers.Any(identifier =>
                string.IsNullOrEmpty(identifier.Value) || identifier.Value.StartsWith('#')))
            throw new QueryPolicyException("Remote, cross-database and temporary objects are forbidden.");

        if (identifiers.Count != 1 || !_visibleCtes.Contains(identifiers[0].Value))
            Tables.Add(new SqlTableIdentity(identifiers.Count == 2 ? identifiers[0].Value : null,
                identifiers[^1].Value));

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(FunctionCall node)
    {
        var metadataFunction = allowMetadata && node.Parameters.Count == 1 &&
            node.FunctionName.Value.Equals("OBJECT_SCHEMA_NAME", StringComparison.OrdinalIgnoreCase);
        if (node.CallTarget is not null ||
            (!AllowedFunctions.Contains(node.FunctionName.Value) && !metadataFunction))
            throw new QueryPolicyException("Only approved built-in scalar and aggregate functions are allowed.");
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(ColumnReferenceExpression node)
    {
        if (node.MultiPartIdentifier?.Identifiers.Count > 2)
            throw new QueryPolicyException("Column references may contain only an alias and column name.");
        base.ExplicitVisit(node);
    }
}
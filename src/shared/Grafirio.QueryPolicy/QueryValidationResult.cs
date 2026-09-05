namespace Grafirio.QueryPolicy;

/// <summary>Physical objects only: CTE names and derived-table aliases are not authorization targets.</summary>
public sealed record QueryValidationResult(IReadOnlyList<SqlTableIdentity> Tables);
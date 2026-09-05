using Grafirio.QueryPolicy;
using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>SQL Server AST policy facade. Execution must use Validate, not only IsReadOnly.</summary>
public static class ReadOnlySqlPolicy
{
    public static QueryValidationResult Validate(
        string sql, IEnumerable<string>? allowedTables = null, bool allowMetadata = false) =>
        SqlPolicy.Validate(sql, allowedTables, allowMetadata);

    public static bool IsReadOnly(string sql)
    {
        try
        {
            SqlPolicy.Inspect(sql, allowMetadata: true);
            return true;
        }
        catch (QueryPolicyException)
        {
            return false;
        }
    }
}

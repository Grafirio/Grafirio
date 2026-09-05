using Grafirio.QueryPolicy;
using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.Bridge;

/// <summary>Local AST validation. Bridge never relies on validation performed in the cloud.</summary>
public static class ReadOnlySql
{
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

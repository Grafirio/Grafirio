using System.Security.Cryptography;
using System.Text;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.QueryPolicy;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

/// <summary>Validates callback authority against persisted query, configuration and connection state.</summary>
public static class InternalQueryScope
{
    private const string ProcessingStatus = "processing";
    private const string ReadyStatus = "ready";
    private const int Sha256HexLength = 64;
    private const string InvalidScopeMessage = "The query execution scope is invalid or no longer active.";

    public static string ComputeConfigHash(string configJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(configJson)));

    public static IReadOnlyList<string> Validate(
        string? companyId, Guid queryId, Guid configId, Guid connectionId, string? configHash,
        QueryHistory? query, AnalysisConfig? config, SavedConnection? connection,
        IEnumerable<string>? selectedTables)
    {
        if (string.IsNullOrWhiteSpace(companyId) || queryId == Guid.Empty ||
            configId == Guid.Empty || connectionId == Guid.Empty ||
            configHash is null || configHash.Length != Sha256HexLength ||
            configHash.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            query is null || config is null || connection is null ||
            query.Id != queryId || query.ConfigId != configId || query.Status != ProcessingStatus ||
            config.Id != configId || config.CompanyId != companyId || config.ConnectionId != connectionId ||
            !config.IsActive || config.Status != ReadyStatus ||
            connection.Id != connectionId || connection.CompanyId != companyId || !connection.IsActive ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(configHash), Encoding.ASCII.GetBytes(ComputeConfigHash(config.ConfigJson))))
            throw new QueryPolicyException(InvalidScopeMessage, true);

        return QueryTableScope.RequireCurrentConfig(config.TablesJson, selectedTables);
    }
}
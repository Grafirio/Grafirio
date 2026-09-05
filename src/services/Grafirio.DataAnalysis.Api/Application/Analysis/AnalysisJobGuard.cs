using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

public static class AnalysisJobGuard
{
    private const string AnalyzingStatus = "analyzing";
    public static bool MatchesSelection(string snapshot, IReadOnlyList<string> current)
    {
        try
        {
            var selected = JsonSerializer.Deserialize<List<string>>(snapshot);
            if (selected is null || selected.Count == 0 || current.Count == 0
                || selected.Any(string.IsNullOrWhiteSpace) || current.Any(string.IsNullOrWhiteSpace)) return false;
            var expected = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return expected.Count == selected.Count && current.Distinct(StringComparer.OrdinalIgnoreCase).Count() == current.Count
                && expected.SetEquals(current);
        }
        catch (JsonException) { return false; }
    }

    public static bool CanRun(Guid configConnectionId, string configCompanyId, bool configActive, string status,
        Guid connectionId, string connectionCompanyId, bool connectionActive, Guid requestedConnectionId, string requestedCompanyId) =>
        configActive && connectionActive && status == AnalyzingStatus
        && configConnectionId == requestedConnectionId && connectionId == requestedConnectionId
        && configCompanyId == requestedCompanyId && connectionCompanyId == requestedCompanyId;
}
using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data.Entities;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

public sealed record PyCaretExecutionContext(
    Guid QueryId, string CompanyId, Guid ConnectionId, Guid ConfigId, string ConfigHash)
{
    public const string PropertyName = "executionContext";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PyCaretExecutionContext Create(QueryHistory query, AnalysisConfig config) =>
        new(query.Id, config.CompanyId, config.ConnectionId, config.Id,
            InternalQueryScope.ComputeConfigHash(config.ConfigJson));

    public static PyCaretExecutionContext? Read(QueryHistory query) =>
        (JsonNode.Parse(query.ResultJson) as JsonObject)?[PropertyName]?.Deserialize<PyCaretExecutionContext>(JsonOptions);

    public JsonNode ToJson() => JsonSerializer.SerializeToNode(this, JsonOptions)!;

    public bool Matches(JsonObject response) =>
        Text(response, "query_id") == QueryId.ToString() &&
        Text(response, "company_id") == CompanyId &&
        Text(response, "connection_id") == ConnectionId.ToString() &&
        Text(response, "config_id") == ConfigId.ToString() &&
        Text(response, "config_hash") == ConfigHash;

    public static string? Text(JsonObject value, string name) =>
        value[name] is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;
}
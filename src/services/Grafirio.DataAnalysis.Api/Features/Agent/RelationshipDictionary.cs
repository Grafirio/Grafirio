using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Profile;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

public static class RelationshipDictionary
{
    public const string ProposalsProperty = "relationship_proposals";
    public const string RejectedProperty = "rejectedRelationships";
    public const int MaxProposals = 8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static List<RelationshipProposal> ReadProposals(string translationJson, string dictionaryJson)
    {
        JsonObject translation;
        try
        {
            translation = JsonNode.Parse(translationJson) as JsonObject
                ?? throw new ArgumentException("Analiz parametreleri bir JSON nesnesi olmalıdır.");
        }
        catch (JsonException)
        {
            throw new ArgumentException("Analiz parametreleri okunamadı.");
        }
        if (!translation.TryGetPropertyValue(ProposalsProperty, out var proposalNode) || proposalNode is null) return [];
        if (proposalNode is not JsonArray proposals)
            throw new ArgumentException("Eşleşme önerileri bir liste olmalıdır.");
        if (proposals.Count > MaxProposals)
            throw new ArgumentException("Bir turda en fazla sekiz eşleşme önerilebilir.");

        var dictionary = JsonNode.Parse(dictionaryJson)!.AsObject();
        var rejected = ReadRejected(dictionary).Select(p => p.Key).ToHashSet();
        var result = new List<RelationshipProposal>();
        foreach (var node in proposals)
        {
            RelationshipProposal? proposal;
            try { proposal = node?.Deserialize<RelationshipProposal>(JsonOptions); }
            catch (JsonException) { throw new ArgumentException("Eşleşme önerisinin biçimi geçersiz."); }
            if (proposal is null || !HasColumn(dictionary, proposal.FromTable, proposal.FromColumn)
                || !HasColumn(dictionary, proposal.ToTable, proposal.ToColumn))
                throw new ArgumentException("Önerilen tablo veya kolon analiz sözlüğünde bulunamadı; eşleşme onaya sunulmadı.");
            if (rejected.Contains(proposal.Key))
                throw new ArgumentException("Bu eşleşme daha önce reddedilmiş. Kararı değiştirmek için bağlantı ayarlarını kullanın.");
            if (!result.Any(existing => existing.Key == proposal.Key)) result.Add(proposal);
        }
        return result;
    }

    public static string Apply(string dictionaryJson, LearnedFact fact, RelationshipProfile? relationship)
    {
        var root = JsonNode.Parse(dictionaryJson)!.AsObject();
        var edges = root["relationships"] as JsonArray ?? new JsonArray();
        if (edges.Parent is null) root["relationships"] = edges;
        for (var index = edges.Count - 1; index >= 0; index--)
        {
            var edge = edges[index]?.Deserialize<RelationshipProfile>(JsonOptions);
            if (edge is not null && edge.FromColumns.Count == 1 && edge.ToColumns.Count == 1
                && LearnedFact.RelationshipKey(edge.FromTable, edge.FromColumns[0], edge.ToTable, edge.ToColumns[0]) == fact.Key)
                edges.RemoveAt(index);
        }

        if (fact.Accepted)
        {
            ArgumentNullException.ThrowIfNull(relationship);
            relationship.NeedsConfirmation = false;
            edges.Add(JsonSerializer.SerializeToNode(relationship, JsonOptions));
        }

        var rejected = ReadRejected(root).Where(p => p.Key != fact.Key).ToList();
        if (!fact.Accepted)
            rejected.Add(new(fact.FromTable!, fact.FromColumn!, fact.ToTable!, fact.ToColumn!));
        root[RejectedProperty] = JsonSerializer.SerializeToNode(rejected, JsonOptions);
        UpdateStats(root, edges);
        return root.ToJsonString(JsonOptions);
    }

    public static string Forget(string dictionaryJson, string key)
    {
        var root = JsonNode.Parse(dictionaryJson)!.AsObject();
        var changed = false;
        if (root["relationships"] is JsonArray edges)
        {
            for (var index = edges.Count - 1; index >= 0; index--)
            {
                var edge = edges[index]?.Deserialize<RelationshipProfile>(JsonOptions);
                // Forget only the user's exact single-column declaration, not catalog or inferred evidence.
                if (edge is not { Source: "declared" } || edge.FromColumns.Count != 1 || edge.ToColumns.Count != 1
                    || LearnedFact.RelationshipKey(edge.FromTable, edge.FromColumns[0], edge.ToTable, edge.ToColumns[0]) != key)
                    continue;
                edges.RemoveAt(index);
                changed = true;
            }
            if (changed) UpdateStats(root, edges);
        }

        if (root[RejectedProperty] is JsonArray rejections)
        {
            for (var index = rejections.Count - 1; index >= 0; index--)
            {
                if (rejections[index]?.Deserialize<RelationshipProposal>(JsonOptions)?.Key != key) continue;
                rejections.RemoveAt(index);
                changed = true;
            }
        }
        return changed ? root.ToJsonString(JsonOptions) : dictionaryJson;
    }

    private static void UpdateStats(JsonObject root, JsonArray edges)
    {
        if (root["profileStats"] is JsonObject stats)
        {
            stats["relationshipCount"] = edges.Count;
            stats["inferredRelationshipCount"] = edges.Count(e => e?["source"]?.GetValue<string>() == "inferred");
        }
    }

    private static List<RelationshipProposal> ReadRejected(JsonObject root) =>
        root[RejectedProperty]?.Deserialize<List<RelationshipProposal>>(JsonOptions) ?? [];

    public static Dictionary<string, bool> AppliedDecisions(string dictionaryJson)
    {
        var root = JsonNode.Parse(dictionaryJson)!.AsObject();
        var decisions = ReadRejected(root).ToDictionary(p => p.Key, _ => false);
        if (root["relationships"] is not JsonArray edges) return decisions;
        foreach (var node in edges)
        {
            var edge = node?.Deserialize<RelationshipProfile>(JsonOptions);
            if (edge is { Source: "declared", NeedsConfirmation: false }
                && edge.FromColumns.Count == 1 && edge.ToColumns.Count == 1)
                decisions[LearnedFact.RelationshipKey(edge.FromTable, edge.FromColumns[0], edge.ToTable, edge.ToColumns[0])] = true;
        }
        return decisions;
    }

    private static bool HasColumn(JsonObject root, string? table, string? column)
    {
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column)) return false;
        return root["columns"] is JsonArray columns && columns.Any(c =>
            Same(c?["table"]?.GetValue<string>(), table)
            && Same(c?["column"]?.GetValue<string>(), column));
    }

    private static bool Same(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
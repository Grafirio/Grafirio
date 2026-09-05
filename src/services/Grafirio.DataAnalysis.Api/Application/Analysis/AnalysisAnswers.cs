using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data.Mongo;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

/// <summary>Validates the entire pending questionnaire before applying any answer.</summary>
public static class AnalysisAnswers
{
    private const int MaximumAnswerLength = 4000;

    public static JsonObject Read(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Dictionary must be an object.");
        if (root["technicalSchemaComplete"]?.GetValue<bool>() != true)
            throw new JsonException("Technical schema is not complete. Reanalysis is required.");
        var tables = CanonicalSchemaDictionary.Objects(root, "tables").ToList();
        var columns = CanonicalSchemaDictionary.Objects(root, "columns").ToList();
        if (tables.Count == 0 || tables.Any(table => !columns.Any(column =>
            Same(column, "table", CanonicalSchemaDictionary.Text(table, "name")))))
            throw new JsonException("Incomplete dictionary.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in CanonicalSchemaDictionary.Objects(root, "questions"))
        {
            var id = CanonicalSchemaDictionary.Text(question, "id");
            if (!ids.Add(id)) throw new JsonException("Duplicate question id.");
            CanonicalSchemaDictionary.Text(question, "question");
            var table = CanonicalSchemaDictionary.Text(question, "table");
            var column = CanonicalSchemaDictionary.OptionalText(question, "column");
            if (!tables.Any(item => Same(item, "name", table))
                || column is not null && !columns.Any(item => Same(item, "table", table) && Same(item, "column", column)))
                throw new JsonException("Question refers to an unknown field.");
        }
        return root;
    }

    public static int CountPending(string json) => ((JsonArray)Read(json)["questions"]!).Count;

    public static string Apply(string json, IReadOnlyDictionary<string, string>? answers,
        out List<LearnedFact> learned, out int remainingCount)
    {
        if (answers is null || answers.Count == 0) throw new ArgumentException("At least one answer is required.");
        var root = Read(json);
        var questions = CanonicalSchemaDictionary.Objects(root, "questions").ToList();
        var ids = questions.Select(question => CanonicalSchemaDictionary.Text(question, "id")).ToHashSet(StringComparer.Ordinal);
        if (answers.Any(answer => !ids.Contains(answer.Key) || string.IsNullOrWhiteSpace(answer.Value)
            || answer.Value.Length > MaximumAnswerLength))
            throw new ArgumentException("Answers must refer to pending questions and contain valid nonempty text.");

        learned = [];
        var remaining = new JsonArray();
        foreach (var question in questions)
        {
            if (!answers.TryGetValue(CanonicalSchemaDictionary.Text(question, "id"), out var answer))
            {
                remaining.Add(question.DeepClone());
                continue;
            }
            var table = CanonicalSchemaDictionary.Text(question, "table");
            var column = CanonicalSchemaDictionary.OptionalText(question, "column");
            var target = column is null
                ? CanonicalSchemaDictionary.Objects(root, "tables").Single(item => Same(item, "name", table))
                : CanonicalSchemaDictionary.Objects(root, "columns").Single(item => Same(item, "table", table) && Same(item, "column", column));
            target[column is null ? "purpose" : "meaning"] = answer.Trim();
            target["confidence"] = "high";
            target["source"] = "user";
            learned.Add(LearnedFact.ForMeaning(table, column, answer.Trim(),
                question: CanonicalSchemaDictionary.Text(question, "question")));
        }
        remainingCount = remaining.Count;
        root["questions"] = remaining;
        root["questionCount"] = remainingCount;
        return root.ToJsonString();
    }

    private static bool Same(JsonObject item, string field, string value) =>
        string.Equals(CanonicalSchemaDictionary.Text(item, field), value, StringComparison.OrdinalIgnoreCase);
}
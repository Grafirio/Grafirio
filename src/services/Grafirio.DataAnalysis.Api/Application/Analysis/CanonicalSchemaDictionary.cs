using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Features.Profile;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

/// <summary>Builds technical facts exclusively from the live catalog; model output supplies annotations only.</summary>
public static class CanonicalSchemaDictionary
{
    public const string Unknown = "unknown";
    public const int MaxSetupQuestions = 8;
    private static readonly HashSet<string> Roles = ["measure", "dimension", "date", "identifier", "other"];
    private static readonly HashSet<string> Confidences = ["high", "medium", "low"];

    public static JsonObject Build(string json, DatabaseProfile profile)
    {
        ValidateProfile(profile);
        var source = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Dictionary must be an object.");
        var annotations = Objects(source, "tables").ToList();
        var columnAnnotations = Objects(source, "columns").ToList();
        foreach (var table in annotations)
            RequireTable(profile, Text(table, "name"));
        foreach (var column in columnAnnotations)
            RequireColumn(RequireTable(profile, Text(column, "table")), Text(column, "column"));

        var tables = new JsonArray();
        var columns = new JsonArray();
        foreach (var table in profile.Tables)
        {
            var annotation = annotations.FirstOrDefault(item => Same(Text(item, "name"), table.Qualified));
            var entry = new JsonObject
            {
                ["name"] = table.Qualified, ["schema"] = table.Schema, ["tableName"] = table.TableName,
                ["approximateRowCount"] = table.ApproximateRowCount, ["sampledRowCount"] = table.SampledRowCount
            };
            Annotate(entry, annotation, "purpose");
            tables.Add(entry);
            foreach (var column in table.Columns)
            {
                var meaning = columnAnnotations.FirstOrDefault(item => Same(Text(item, "table"), table.Qualified)
                    && Same(Text(item, "column"), column.ColumnName));
                var technical = new JsonObject
                {
                    ["table"] = table.Qualified, ["column"] = column.ColumnName, ["dataType"] = column.DataType,
                    ["isNullable"] = column.IsNullable, ["maxLength"] = column.MaxLength,
                    ["isPrimaryKey"] = column.IsPrimaryKey,
                    ["distinctCount"] = column.DistinctCount, ["nullCount"] = column.NullCount,
                    ["statsFromSample"] = column.StatsFromSample,
                    ["role"] = ValidChoice(meaning, "role", Roles, "other")
                };
                Annotate(technical, meaning, "meaning");
                columns.Add(technical);
            }
        }

        var questions = new JsonArray();
        var targets = new HashSet<(string, string)>();
        foreach (var question in Objects(source, "questions"))
        {
            var table = RequireTable(profile, Text(question, "table"));
            var columnName = OptionalText(question, "column");
            var column = columnName is null ? null : RequireColumn(table, columnName).ColumnName;
            var text = Text(question, "question");
            if (!targets.Add((table.Qualified.ToUpperInvariant(), column?.ToUpperInvariant() ?? ""))) continue;
            questions.Add(new JsonObject
            {
                ["id"] = $"q{questions.Count + 1}", ["table"] = table.Qualified,
                ["column"] = column, ["question"] = text, ["options"] = StringArray(question, "options")
            });
        }

        var generatedQuestionCount = questions.Count;
        // Only this persisted, bounded set is mandatory; omitted annotations remain explicitly unknown.
        var displayed = new JsonArray(questions.OfType<JsonObject>()
            .OrderBy(question => question["column"] is null ? 0 : 1)
            .Take(MaxSetupQuestions).Select(question => question.DeepClone()).ToArray());
        return new JsonObject
        {
            ["sector"] = OptionalText(source, "sector") ?? Unknown,
            ["sectorConfidence"] = ValidChoice(source, "sectorConfidence", Confidences, "low"),
            ["tables"] = tables, ["columns"] = columns, ["questions"] = displayed,
            ["questionCount"] = displayed.Count, ["generatedQuestionCount"] = generatedQuestionCount,
            ["technicalSchemaComplete"] = true
        };
    }

    public static void ValidateProfile(DatabaseProfile profile)
    {
        if (profile.Tables.Count == 0 || profile.Tables.Any(table => table.Error is not null || table.Columns.Count == 0))
            throw new InvalidOperationException("Every selected table must have a complete live profile.");
        if (profile.Tables.Select(table => table.Qualified).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profile.Tables.Count)
            throw new InvalidOperationException("Duplicate live table.");
        foreach (var table in profile.Tables)
            if (table.Columns.Any(column => string.IsNullOrWhiteSpace(column.ColumnName) || string.IsNullOrWhiteSpace(column.DataType))
                || table.Columns.Select(column => column.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != table.Columns.Count)
                throw new InvalidOperationException("Invalid live column metadata.");
    }

    private static void Annotate(JsonObject target, JsonObject? source, string field)
    {
        target[field] = source is null ? Unknown : OptionalText(source, field) ?? Unknown;
        target["confidence"] = source is null || target[field]!.GetValue<string>() == Unknown
            ? "low" : ValidChoice(source, "confidence", Confidences, "low");
        target["synonyms"] = source is null ? new JsonArray() : StringArray(source, "synonyms");
        target["source"] = source is null ? "unknown" : "model";
    }

    internal static IEnumerable<JsonObject> Objects(JsonObject root, string name)
    {
        if (root[name] is not JsonArray array) throw new JsonException($"{name} must be an array.");
        return array.Select(item => item as JsonObject ?? throw new JsonException($"Invalid {name} entry."));
    }

    internal static string Text(JsonObject item, string name) =>
        OptionalText(item, name) ?? throw new JsonException($"Missing {name}.");

    internal static string? OptionalText(JsonObject item, string name)
    {
        if (item[name] is null) return null;
        if (item[name] is not JsonValue value || !value.TryGetValue<string>(out var text))
            throw new JsonException($"{name} must be text.");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static JsonArray StringArray(JsonObject item, string name)
    {
        if (item[name] is null) return [];
        if (item[name] is not JsonArray array) throw new JsonException($"{name} must be an array.");
        foreach (var value in array)
            if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
                throw new JsonException($"Invalid {name} value.");
        return (JsonArray)array.DeepClone();
    }

    private static string ValidChoice(JsonObject? item, string name, HashSet<string> allowed, string fallback)
    {
        var value = item is null ? null : OptionalText(item, name);
        if (value is null) return fallback;
        if (!allowed.Contains(value)) throw new JsonException($"Invalid {name}.");
        return value;
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static TableProfile RequireTable(DatabaseProfile profile, string name) =>
        profile.Tables.FirstOrDefault(table => Same(table.Qualified, name)) ?? throw new JsonException("Unknown table.");
    private static ColumnProfile RequireColumn(TableProfile table, string name) =>
        table.Columns.FirstOrDefault(column => Same(column.ColumnName, name)) ?? throw new JsonException("Unknown column.");
}
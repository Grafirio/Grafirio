using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Grafirio.DataAnalysis.Api.Application.Analysis;

/// <summary>Checks explicit name/code wording against dictionary-backed filter columns before execution.</summary>
public sealed class ExplicitEntityFilterPolicy
{
    private sealed record Column(string Entity, string Table, string Name, bool IsName, bool IsCode, string Semantics, bool IsPrimary);
    private sealed record Intent(string Entity, string Label, bool ByName, string CodeKind);
    private readonly List<Column> columns;
    private readonly List<Intent> intents;
    private readonly string question;
    private static readonly (string Key, string Label, string English)[] Entities =
    [ ("urun", "ürün", "product"), ("musteri", "müşteri", "customer"),
      ("tedarikci", "tedarikçi", "supplier"), ("depo", "depo", "warehouse"),
      ("kategori", "kategori", "category"), ("marka", "marka", "brand") ];
    private static readonly Regex NameWords = new(@"\b(ad|adi|isim|ismi|name|title|unvan|unvani)\b", RegexOptions.Compiled);
    private static readonly Regex CodeWords = new(@"\b(sku|kod|kodu|code|kimlik|id|identifier|barkod|barcode)\b", RegexOptions.Compiled);

    private ExplicitEntityFilterPolicy(List<Column> columns, List<Intent> intents, string question)
    { this.columns = columns; this.intents = intents; this.question = question; }

    public static ExplicitEntityFilterPolicy From(string question, string dictionaryJson)
    {
        var normalized = Normalize(question);
        var intents = new List<Intent>();
        var columns = new List<Column>();
        JsonObject? dictionary;
        try { dictionary = JsonNode.Parse(dictionaryJson) as JsonObject; }
        catch (System.Text.Json.JsonException) { return new([], [], normalized); }
        if (dictionary?["columns"] is not JsonArray schema) return new([], [], normalized);

        foreach (var entity in Entities)
        {
            var byName = Regex.IsMatch(normalized, $@"\b(?:adli|isimli)\s+(?:bir\s+)?{entity.Key}\p{{L}}*\b|\b{entity.Key}(?:un|in|nin)?\s+(?:adi|ismi)\b");
            var byCode = Regex.IsMatch(normalized, $@"\b{entity.Key}\s+(?:kodu|kimligi|id(?:si)?)\b");
            if (entity.Key == "urun") byCode |= Regex.IsMatch(normalized, @"\b(sku(?:su)?|stok kodu|barkod(?:u)?)\b");
            // Explicit named wording wins even when the name happens to contain 'SKU' or a number.
            if (!byName && !byCode) continue;
            var codeKind = Regex.IsMatch(normalized, @"\bsku(?:su)?\b") ? "sku"
                : Regex.IsMatch(normalized, @"\bbarkod(?:u)?\b") ? "barcode"
                : Regex.IsMatch(normalized, $@"\b{entity.Key}\s+(?:kimligi|id(?:si)?)\b") ? "id" : "code";
            intents.Add(new(entity.Key, entity.Label, byName, codeKind));
            foreach (var node in schema.OfType<JsonObject>())
            {
                var table = Text(node["table"]); var name = Text(node["column"]);
                if (table.Length == 0 || name.Length == 0) continue;
                var semantics = Normalize(Text(node["meaning"]) + " " + Texts(node["synonyms"]));
                var physical = Normalize(Regex.Replace(name, @"(\p{Ll})(\p{Lu})", "$1 $2"));
                var tableSemantics = Normalize(table);
                if (dictionary["tables"] is JsonArray tables)
                {
                    var entry = tables.OfType<JsonObject>().FirstOrDefault(t => Text(t["name"]) == table);
                    if (entry is not null) tableSemantics += " " + Normalize(Text(entry["purpose"]) + " " + Texts(entry["synonyms"]));
                }
                if (!semantics.Contains(entity.Key, StringComparison.Ordinal)
                    && !tableSemantics.Contains(entity.Key, StringComparison.Ordinal)
                    && !tableSemantics.Contains(entity.English, StringComparison.Ordinal)
                    && !physical.Contains(entity.Key, StringComparison.Ordinal)
                    && !physical.Contains(entity.English, StringComparison.Ordinal)) continue;
                var isCode = CodeWords.IsMatch(physical) || Text(node["role"]) == "identifier" || CodeWords.IsMatch(semantics);
                var directName = Regex.IsMatch(semantics + " " + physical, $@"\b{entity.Key}(?:un|in|nin)?\s+(adi|ismi|ad|isim|unvan|unvani|name|title)\b|\b{entity.English}\s+name\b");
                var tableLeaf = Normalize(Regex.Replace(table.Split('.').Last(), @"(\p{Ll})(\p{Lu})", "$1 $2"));
                var directTable = Regex.IsMatch(tableLeaf, $@"^({entity.Key}|{entity.English})(s|ler|lar)?$");
                var isName = !isCode && (NameWords.IsMatch(semantics) || NameWords.IsMatch(physical)) && (directName || directTable);
                columns.Add(new(entity.Key, table, name, isName, isCode, physical + " " + semantics,
                    node["isPrimaryKey"] is JsonValue primary && primary.TryGetValue<bool>(out var isPrimary) && isPrimary));
            }
        }
        return new(columns.Distinct().ToList(), intents, normalized);
    }

    public string PromptHints => intents.Count == 0 ? "" : "\n## Açık ad/kod seçimi — zorunlu doğrulama\n"
        + string.Join("\n", intents.Select(i => $"Kullanıcı {i.Label} {(i.ByName ? "adını" : "kodunu/kimliğini")} açıkça belirtti. "
            + $"Filtre için sözlükteki uygun alanlar: {string.Join(", ", columns.Where(c => Matches(c, i)).Select(c => c.Table + "." + c.Name))}. "
            + "Adın içindeki rakamları ayırıp başka bir kod/kimlik alanına taşıma; adı bütün olarak koru."));

    public string? Validate(string parametersJson)
    {
        if (intents.Count == 0) return null;
        JsonObject? root;
        try { root = JsonNode.Parse(parametersJson) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; } // Existing JSON validation owns malformed responses.
        if (root is null || string.IsNullOrWhiteSpace(Text(root["target_table"]))) return null; // Clarification, no execution.
        return ValidateBranch(root, "target_table");
    }

    private string? ValidateBranch(JsonObject branch, string tableKey)
    {
        var tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["base"] = Text(branch[tableKey]) };
        if (branch["joins"] is JsonArray joins)
            foreach (var join in joins.OfType<JsonObject>())
            {
                var alias = Text(join["as"]);
                tables[alias.Length > 0 ? alias : $"join{tables.Count}"] = Text(join["table"]);
            }
        var used = tables.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = new List<(Column Column, JsonNode? Value)>();
        if (branch["filters"] is JsonObject filters)
            foreach (var filter in filters)
            {
                var parts = filter.Key.Split('.').Select(p => p.Trim('[', ']', '"')).ToArray();
                foreach (var column in columns.Where(c => used.Contains(c.Table) && string.Equals(c.Name, parts[^1], StringComparison.OrdinalIgnoreCase)))
                {
                    var qualifier = string.Join('.', parts[..^1]);
                    if (parts.Length == 1 || string.Equals(qualifier, column.Table, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(qualifier, column.Table.Split('.').Last(), StringComparison.OrdinalIgnoreCase)
                        || tables.TryGetValue(qualifier, out var target) && string.Equals(target, column.Table, StringComparison.OrdinalIgnoreCase))
                        filtered.Add((column, filter.Value));
                }
            }
        foreach (var intent in intents)
        {
            var required = columns.Where(c => used.Contains(c.Table) && Matches(c, intent)).ToList();
            if (required.Count == 0)
                return $"Explicit {intent.Label} {(intent.ByName ? "name" : "code")} intent has no matching dictionary-backed column in the query scope.";
            if (!filtered.Any(f => required.Contains(f.Column)))
                return $"Explicit {intent.Label} {(intent.ByName ? "name" : "code")} intent requires a filter on {string.Join(", ", required.Select(c => c.Table + "." + c.Name))}.";
            var literal = intent.ByName ? SimpleNamedLiteral(question, intent.Entity) : null;
            if (literal is not null && filtered.Any(f => required.Contains(f.Column) && !MatchesLiteral(f.Value, literal)))
                return "Explicit named reference must preserve the complete user-provided name, including numbers and punctuation.";
            if (intent.ByName && filtered.Any(f => required.Contains(f.Column) && Text(f.Value).Length > 0
                && Regex.IsMatch(Text(f.Value), @"^\d+$") && NamedLiteralHasWords(question, intent.Entity)))
                return "Explicit named reference was reduced to a number; preserve the complete name including its numeric suffix.";
            if (intent.ByName && !HasSeparateCodeCue(question) && filtered.Any(f => f.Column.Entity == intent.Entity && f.Column.IsCode
                && required.Any(c => c.Table == f.Column.Table)))
                return "Explicit named reference must not introduce an unrequested SKU/code/identity filter.";
        }
        var branches = branch["union"] as JsonArray ?? (branch["union"] as JsonObject)?["with"] as JsonArray;
        if (branches is not null)
            foreach (var child in branches.OfType<JsonObject>())
                if (ValidateBranch(child, "table") is { } error) return error;
        return null;
    }

    private static string? SimpleNamedLiteral(string question, string entity)
    {
        var marker = Regex.Match(question, $@"(?<prefix>[^.!?\n]+?)\b(?:adli|isimli)\s+(?:bir\s+)?{entity}\p{{L}}*\b");
        if (!marker.Success) return null;
        var prefix = Regex.Replace(marker.Groups["prefix"].Value.Trim(), @"^(?:(?:bana|lutfen|su|bir|bu)\s+)+", "")
            .Trim('"', '\'', '“', '”', '‘', '’').Trim();
        // Complex sentences still receive a column guard; don't invent a literal from surrounding instructions.
        if (prefix.Length == 0 || Regex.IsMatch(prefix, @"\b(adli|isimli|icin|gore|olan|bul|hesapla|goster|listele|yil|ay)\b")) return null;
        return prefix;
    }

    private static bool MatchesLiteral(JsonNode? value, string literal)
    {
        if (value is JsonArray array) return array.Count == 1 && MatchesLiteral(array[0], literal);
        return value is JsonValue && Normalize(Text(value)).Trim() == literal;
    }

    private static bool Matches(Column column, Intent intent) => column.Entity == intent.Entity && (intent.ByName ? column.IsName
        : column.IsCode && (intent.CodeKind switch
        {
            "sku" => Regex.IsMatch(column.Semantics, @"\bsku\b"),
            "barcode" => Regex.IsMatch(column.Semantics, @"\b(barkod|barcode)\b"),
            "id" => column.IsPrimary,
            _ => Regex.IsMatch(column.Semantics, @"\b(sku|kod|kodu|code)\b")
        }));

    private static bool NamedLiteralHasWords(string question, string entity)
    {
        var marker = Regex.Match(question, $@"(?<prefix>[^.!?\n]+?)\b(?:adli|isimli)\s+(?:bir\s+)?{entity}\p{{L}}*\b");
        if (!marker.Success) return false;
        var prefix = Regex.Replace(marker.Groups["prefix"].Value.Trim(), @"^(?:(?:bana|lutfen|su|bir|bu)\s+)+", "");
        return Regex.IsMatch(prefix, @"[a-z]");
    }

    private static bool HasSeparateCodeCue(string question) => Regex.IsMatch(question, @"\b(sku(?:su)?|stok kodu|urun kodu|kimligi|barkodu)\s*(?:[:=]|olan|ile|[a-z0-9])");
    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    private static string Texts(JsonNode? node) => node is JsonArray array ? string.Join(" ", array.Select(Text)) : Text(node);
    private static string Normalize(string value)
    {
        var result = new StringBuilder();
        foreach (var c in value.ToLower(CultureInfo.GetCultureInfo("tr-TR")))
            result.Append(c switch { 'ı' => 'i', 'ş' => 's', 'ğ' => 'g', 'ü' => 'u', 'ö' => 'o', 'ç' => 'c', _ => c });
        return result.ToString();
    }
}

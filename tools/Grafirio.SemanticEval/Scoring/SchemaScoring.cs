using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.Measurement.Reporting;
using Grafirio.Measurement.Semantic;

namespace Grafirio.SemanticEval.Scoring;

/// <summary>
/// Schema Understanding: sistemin semayi nasil yorumladigi.
///
/// Iki kaynak var ve ikisi ayri puanlaniyor:
///   * Profil (belirlenimci): hassas kolonlarin korunmasi. LLM'e ihtiyac yok.
///   * Sozluk (LLM): kolon rolleri, tablo amaclari, anlam kapsami.
///
/// Rol dogrulugu "kabul edilen roller" icinde aranir: ayni kolonun birden fazla
/// makul yorumu olabiliyor (CountryCode hem boyut hem kimlik). Hata sayilan,
/// bariz yanlis yorum: tutari boyut, kimligi olcum saymak.
/// </summary>
public static class SchemaScoring
{
    public sealed record DatasetResult(string Dataset, List<Case> Cases, List<Metric> Metrics);

    /// <param name="dictionary">LLM sozlugu; null ise yalnizca belirlenimci kisim puanlanir.</param>
    public static DatasetResult Score(string dataset, SemanticGold gold, DatabaseProfile profile, JsonObject? dictionary)
    {
        var cases = new List<Case>();
        var metrics = new List<Metric>();

        ScoreSensitivity(dataset, gold, profile, cases, metrics);
        if (dictionary is not null)
        {
            ScoreRoles(dataset, gold, dictionary, cases, metrics);
            ScoreTables(dataset, gold, dictionary, cases, metrics);
        }

        return new DatasetResult(dataset, cases, metrics);
    }

    private static void ScoreSensitivity(string dataset, SemanticGold gold, DatabaseProfile profile, List<Case> cases,
        List<Metric> metrics)
    {
        var columns = profile.Tables
            .SelectMany(t => t.Columns.Select(c => (Key: $"{t.Qualified}.{c.ColumnName}", Column: c)))
            .ToDictionary(x => x.Key, x => x.Column, StringComparer.OrdinalIgnoreCase);

        var sensitive = gold.Columns.Where(c => c.Value.Sensitive).Select(c => c.Key).ToList();
        var protectedCount = 0;

        foreach (var key in sensitive)
        {
            // "Korunuyor": degeri okunmuyor ve modele gitmiyor.
            var isProtected = columns.TryGetValue(key, out var column)
                              && column.SamplingDecision == nameof(SensitiveColumnPolicy.Decision.Denied);
            if (isProtected) protectedCount++;

            cases.Add(new Case
            {
                Name = $"{dataset}: {Short(key)}",
                Group = $"{dataset}/hassas-kolon",
                Success = isProtected,
                Message = isProtected
                    ? null
                    : $"Hassas kolon korunmuyor (karar: {column?.SamplingDecision ?? "kolon yok"}). " +
                      "Degerleri ornek olarak okunup modele gidebilir.",
                Details = new { column = key, decision = column?.SamplingDecision, reason = column?.SamplingNote }
            });
        }

        if (sensitive.Count > 0)
            metrics.Add(Metric.Higher("sensitive.recall", protectedCount / (double)sensitive.Count, "ratio"));

        // Asiri koruma: hassas olmayan ama engellenen kolonlar. Anlam cikarmayi
        // zorlastirir (ornek deger yok) ama guvenlik acigi degil; bilgi amacli.
        var plain = gold.Columns.Where(c => !c.Value.Sensitive).Select(c => c.Key).ToList();
        if (plain.Count > 0)
            metrics.Add(Metric.Info("sensitive.over_protection",
                plain.Count(k => columns.TryGetValue(k, out var c)
                                 && c.SamplingDecision == nameof(SensitiveColumnPolicy.Decision.Denied)) / (double)plain.Count,
                "ratio"));
    }

    private static void ScoreRoles(string dataset, SemanticGold gold, JsonObject dictionary, List<Case> cases,
        List<Metric> metrics)
    {
        var annotations = Array(dictionary, "columns")
            .ToDictionary(c => $"{Text(c, "table")}.{Text(c, "column")}", StringComparer.OrdinalIgnoreCase);

        var correct = 0;
        var withMeaning = 0;
        var byRole = new Dictionary<string, (int Correct, int Total)>(StringComparer.Ordinal);

        foreach (var (key, expected) in gold.Columns)
        {
            annotations.TryGetValue(key, out var annotation);
            var role = annotation is null ? "(yok)" : Text(annotation, "role");
            var meaning = annotation is null ? null : Text(annotation, "meaning");
            var ok = expected.Role.Contains(role, StringComparer.OrdinalIgnoreCase);
            if (ok) correct++;
            if (meaning is not null && meaning != "unknown") withMeaning++;

            var preferred = expected.Role[0];
            var (c, t) = byRole.GetValueOrDefault(preferred);
            byRole[preferred] = (c + (ok ? 1 : 0), t + 1);

            cases.Add(new Case
            {
                Name = $"{dataset}: {Short(key)}",
                Group = $"{dataset}/rol-{preferred}",
                Success = ok,
                Message = ok ? null : $"Rol '{role}', beklenen: {string.Join(" | ", expected.Role)}",
                Details = new { column = key, expected = expected.Role, actual = role, meaning,
                    synonyms = annotation?["synonyms"]?.DeepClone() }
            });
        }

        var total = gold.Columns.Count;
        if (total == 0) return;

        metrics.Add(Metric.Higher("role.accuracy", correct / (double)total, "ratio"));
        foreach (var (role, (c, t)) in byRole.OrderBy(p => p.Key, StringComparer.Ordinal))
            metrics.Add(Metric.Higher($"role.recall.{role}", c / (double)t, "ratio"));
        metrics.Add(Metric.Higher("meaning.coverage", withMeaning / (double)total, "ratio"));
    }

    private static void ScoreTables(string dataset, SemanticGold gold, JsonObject dictionary, List<Case> cases,
        List<Metric> metrics)
    {
        var tables = Array(dictionary, "tables").ToDictionary(t => Text(t, "name"), StringComparer.OrdinalIgnoreCase);
        var conceptHits = 0;
        var confusions = 0;

        foreach (var (name, expected) in gold.Tables)
        {
            tables.TryGetValue(name, out var table);
            var purpose = table is null ? "" : Text(table, "purpose");
            var synonyms = table?["synonyms"] is JsonArray array
                ? array.Select(s => s?.GetValue<string>() ?? "").ToList()
                : [];
            var text = Fold(purpose + " " + string.Join(" ", synonyms));

            var hit = expected.Concepts.Count == 0 || expected.Concepts.Any(c => text.Contains(Fold(c), StringComparison.Ordinal));
            var forbidden = expected.Forbidden.Where(f => text.Contains(Fold(f), StringComparison.Ordinal)).ToList();
            if (hit) conceptHits++;
            if (forbidden.Count > 0) confusions++;

            cases.Add(new Case
            {
                Name = $"{dataset}: {Short(name)}",
                Group = $"{dataset}/tablo",
                Success = hit && forbidden.Count == 0,
                Message = forbidden.Count > 0
                    ? $"Karsit kavram geciyor: {string.Join(", ", forbidden)} (amac: {purpose})"
                    : hit ? null : $"Beklenen kavramlarin hicbiri yok (amac: {purpose})",
                Details = new { table = name, purpose, synonyms, expected.Concepts, expected.Forbidden }
            });
        }

        if (gold.Tables.Count == 0) return;
        metrics.Add(Metric.Higher("table.concept_accuracy", conceptHits / (double)gold.Tables.Count, "ratio"));
        metrics.Add(Metric.Lower("table.opposite_confusions", confusions, "count"));
        metrics.Add(Metric.Info("setup_questions",
            dictionary["generatedQuestionCount"]?.GetValue<int>() ?? Array(dictionary, "questions").Count(), "count"));
    }

    /// <summary>Veri setlerinin metrikleri: ayni ad toplam icin ortalama, set basina "@ad".</summary>
    public static List<Metric> Summarize(IReadOnlyList<DatasetResult> results)
    {
        var metrics = new List<Metric>();
        foreach (var group in results.SelectMany(r => r.Metrics).GroupBy(m => m.Name))
        {
            var first = group.First();
            var value = first.Unit == "count" ? group.Sum(m => m.Value) : group.Average(m => m.Value);
            metrics.Add(first with { Value = value });
        }

        foreach (var result in results)
            foreach (var metric in result.Metrics.Where(m => m.Name is "role.accuracy" or "sensitive.recall"
                         or "table.concept_accuracy"))
                metrics.Add(metric with { Name = $"{metric.Name}@{result.Dataset}" });

        return metrics;
    }

    /// <summary>Turkce karakter ve buyuk/kucuk harf farkini kaldirir: "İhracat" = "ihracat".</summary>
    internal static string Fold(string value) => value
        .Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant()
        .Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g').Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c');

    private static IEnumerable<JsonObject> Array(JsonObject root, string name) =>
        root[name] is JsonArray array ? array.OfType<JsonObject>() : [];

    private static string Text(JsonObject item, string name) =>
        item[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

    private static string Short(string qualified) =>
        qualified.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) ? qualified[4..] : qualified;
}

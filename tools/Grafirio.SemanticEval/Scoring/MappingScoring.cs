using System.Text.Json.Nodes;
using Grafirio.Measurement.Evaluation;
using Grafirio.Measurement.Reporting;
using Grafirio.Measurement.Semantic;

namespace Grafirio.SemanticEval.Scoring;

/// <summary>Bir sorunun (ya da bir yeniden yazilisinin) ceviri katmanindaki sonucu.</summary>
/// <param name="Status">
/// completed — hedef tablo secildi ve sozluk kapsaminda gecerli;
/// clarification — model sordu (hedef tablo bos ya da iliski onerisi);
/// rejected — model sozlukte/secimde olmayan bir tablo ya da kolon kullandi (uydurma);
/// failed — ceviri basarisiz (bozuk cevap, LLM hatasi).
/// </param>
public sealed record TranslationOutcome(string Text, string Status, JsonNode? Parameters, string? Error);

/// <summary>
/// Semantic Mapping ve Unknown Data Handling. Ikisi ayni cevirilerden
/// puanlaniyor ama farkli sorulara bakiyor:
///
///   * Esleme — cevaplanabilir sorular: dogru tablo, dogru kolonlar/filtreler,
///     ayni niyetin farkli yazilislari ayni parametreyi uretiyor mu.
///   * Bilinmeyen — cevabi semada olmayan ya da belirsiz sorular: sistem
///     sordu mu (dogru), yoksa bir sey uydurup cevapladi mi (en kotu hata).
///
/// Cevaplanabilir sorular ayni zamanda bilinmeyen olcumunun kontrol grubu:
/// "her seye soru soran" bir sistem bilinmeyenlerde kusursuz gorunur, ama
/// cevaplanabilir sorularda gereksiz soru orani bunu ortaya cikarir.
/// </summary>
public static class MappingScoring
{
    public sealed record Result(List<Case> MappingCases, List<Metric> MappingMetrics, List<Case> UnknownCases,
        List<Metric> UnknownMetrics);

    public static Result Score(IReadOnlyList<(string Dataset, SemanticQuestion Question, List<TranslationOutcome> Outcomes)> items)
    {
        var mappingCases = new List<Case>();
        var unknownCases = new List<Case>();

        var answerable = items.Where(i => i.Question.Kind == "answerable").ToList();
        var unknown = items.Where(i => i.Question.Kind == "unknown").ToList();

        // --- Esleme --------------------------------------------------------------
        var tableHits = 0;
        var fullHits = 0;
        var attempts = 0;
        var falseAbstentions = 0;
        var consistency = new List<double>();

        foreach (var (dataset, question, outcomes) in answerable)
        {
            var perAttempt = outcomes.Select(o =>
            {
                var mismatches = new List<string>();
                var full = EvalScoring.Evaluate(question.Expect, o.Status, o.Parameters, mismatches);
                var tableOk = TableCorrect(question.Expect, o);
                return (Outcome: o, Full: full, Table: tableOk, Mismatches: mismatches);
            }).ToList();

            attempts += perAttempt.Count;
            tableHits += perAttempt.Count(a => a.Table);
            fullHits += perAttempt.Count(a => a.Full);
            falseAbstentions += perAttempt.Count(a => a.Outcome.Status is "clarification" or "rejected");

            // Ifade tutarliligi: ayni niyetin yazilislari ayni parametreyi uretmeli.
            // Yalnizca birden fazla yazilisi olan sorularda anlamli.
            double? paraphraseConsistency = null;
            if (outcomes.Count > 1)
            {
                paraphraseConsistency = EvalScoring.Consistency(outcomes.Select(o =>
                    o.Parameters is null ? o.Status : JsonMatch.Fingerprint(Core(o.Parameters))));
                consistency.Add(paraphraseConsistency.Value);
            }

            var ok = perAttempt.All(a => a.Full);
            var caseMetrics = new List<Metric>
            {
                Metric.Higher("accuracy", perAttempt.Count(a => a.Full) / (double)perAttempt.Count, "ratio"),
                Metric.Higher("table.accuracy", perAttempt.Count(a => a.Table) / (double)perAttempt.Count, "ratio")
            };
            if (paraphraseConsistency is { } pc) caseMetrics.Add(Metric.Higher("paraphrase.consistency", pc, "ratio"));

            mappingCases.Add(new Case
            {
                Name = $"{dataset}: {question.Id}",
                Group = $"{dataset}/{(question.Group.Length > 0 ? question.Group : "genel")}",
                Success = ok,
                Message = ok ? null : string.Join(" | ", perAttempt.Where(a => !a.Full)
                    .Select(a => $"\"{Trim(a.Outcome.Text)}\": " +
                                 (a.Outcome.Error ?? string.Join("; ", a.Mismatches)))
                    .Distinct().Take(3)),
                Metrics = caseMetrics,
                Details = new
                {
                    question.Question, expect = question.Expect,
                    attempts = perAttempt.Select(a => new
                    {
                        a.Outcome.Text, a.Outcome.Status, a.Full, a.Table, a.Mismatches, a.Outcome.Error,
                        parameters = a.Outcome.Parameters
                    })
                }
            });
        }

        var mappingMetrics = new List<Metric>();
        if (attempts > 0)
        {
            mappingMetrics.Add(Metric.Higher("accuracy", fullHits / (double)attempts, "ratio"));
            mappingMetrics.Add(Metric.Higher("table.accuracy", tableHits / (double)attempts, "ratio"));
            mappingMetrics.Add(Metric.Higher("pass.rate",
                mappingCases.Count(c => c.Success) / (double)mappingCases.Count, "ratio"));
            mappingMetrics.Add(Metric.Lower("false_abstention.rate", falseAbstentions / (double)attempts, "ratio"));
            if (consistency.Count > 0)
                mappingMetrics.Add(Metric.Higher("paraphrase.consistency", consistency.Average(), "ratio"));
            mappingMetrics.Add(Metric.Info("questions", attempts, "count"));
            AddPerDataset(mappingMetrics, mappingCases, "pass.rate");
        }

        // --- Bilinmeyen veri -----------------------------------------------------
        var total = 0;
        var abstained = 0;
        var hallucinated = 0;
        var byType = new Dictionary<string, (int Ok, int Total)>(StringComparer.Ordinal);

        foreach (var (dataset, question, outcomes) in unknown)
        {
            foreach (var outcome in outcomes)
            {
                total++;
                // Sormak da, uydurma bir kolonu reddetmek de dogru davranis: ikisinde
                // de kullanici yanlis bir grafik gormuyor.
                var ok = outcome.Status is "clarification" or "rejected";
                if (ok) abstained++;
                if (outcome.Status == "completed") hallucinated++;

                var type = question.UnknownType ?? "belirsiz";
                var (o, t) = byType.GetValueOrDefault(type);
                byType[type] = (o + (ok ? 1 : 0), t + 1);
            }

            var allOk = outcomes.All(o => o.Status is "clarification" or "rejected");
            unknownCases.Add(new Case
            {
                Name = $"{dataset}: {question.Id}",
                Group = $"{dataset}/{question.UnknownType ?? "belirsiz"}",
                Success = allOk,
                Message = allOk ? null : string.Join(" | ", outcomes.Where(o => o.Status == "completed")
                    .Select(o => $"\"{Trim(o.Text)}\" soru sorulmadan cevaplandi: " +
                                 $"{Trim(o.Parameters?.ToJsonString() ?? "", 160)}")
                    .Concat(outcomes.Where(o => o.Status == "failed").Select(o => $"ceviri basarisiz: {o.Error}"))
                    .Take(3)),
                Metrics =
                [
                    Metric.Higher("abstention", outcomes.Count(o => o.Status is "clarification" or "rejected")
                                                / (double)Math.Max(1, outcomes.Count), "ratio")
                ],
                Details = new { question.Question, question.UnknownType, outcomes }
            });
        }

        var unknownMetrics = new List<Metric>();
        if (total > 0)
        {
            unknownMetrics.Add(Metric.Higher("abstention.recall", abstained / (double)total, "ratio"));
            unknownMetrics.Add(Metric.Lower("hallucination.rate", hallucinated / (double)total, "ratio"));
            if (attempts > 0)
                unknownMetrics.Add(Metric.Lower("false_abstention.rate", falseAbstentions / (double)attempts, "ratio"));
            foreach (var (type, (o, t)) in byType.OrderBy(p => p.Key, StringComparer.Ordinal))
                unknownMetrics.Add(Metric.Higher($"abstention.recall.{type}", o / (double)t, "ratio"));
            unknownMetrics.Add(Metric.Info("questions", total, "count"));
        }

        return new Result(mappingCases, mappingMetrics, unknownCases, unknownMetrics);
    }

    /// <summary>
    /// Tutarlilik icin parmak izine giren cekirdek: tablo, olcum, toplama,
    /// kirilim, filtre, join, limit. Grafik basligi ve aciklama gibi serbest
    /// metinler her calistirmada degisir ve anlami degistirmez.
    /// </summary>
    internal static JsonNode Core(JsonNode parameters)
    {
        if (parameters is not JsonObject source) return parameters.DeepClone();
        var core = new JsonObject();
        foreach (var key in new[] { "analysis_type", "target_table", "target_column", "aggregation", "group_by",
                     "filters", "joins", "limit", "having", "window", "union" })
        {
            var match = source.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            if (match.Key is not null && match.Value is not null) core[key] = match.Value.DeepClone();
        }
        return core;
    }

    private static bool TableCorrect(EvalExpectation expect, TranslationOutcome outcome)
    {
        var expected = expect.Params?["target_table"];
        if (expected is null) return outcome.Status == (expect.Status ?? "completed");
        return outcome.Status == "completed"
               && JsonMatch.IsSubset(new JsonObject { ["target_table"] = expected.DeepClone() }, outcome.Parameters, []);
    }

    private static void AddPerDataset(List<Metric> metrics, List<Case> cases, string name)
    {
        foreach (var group in cases.GroupBy(c => c.Name.Split(':')[0]))
            metrics.Add(Metric.Higher($"{name}@{group.Key}", group.Count(c => c.Success) / (double)group.Count(), "ratio"));
    }

    private static string Trim(string value, int max = 60) => value.Length <= max ? value : value[..max] + "…";
}

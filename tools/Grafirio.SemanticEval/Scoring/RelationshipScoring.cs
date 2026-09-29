using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.Measurement.Reporting;
using Grafirio.Measurement.Semantic;

namespace Grafirio.SemanticEval.Scoring;

/// <summary>
/// Relationship Discovery: bulunan kenarlar altin kenar kumesiyle.
///
/// Esleme KATI: kaynak kolon ve hedef kolon ikisi de dogru olmali. Dogru
/// tabloya ama yanlis kolona giden bir kenar join'i yine bozar; kismi puan
/// verilmiyor. Yon de onemli (A.x -> B.y ile B.y -> A.x ayni kenar degil):
/// ters yon cok-bir yerine bir-cok join demek ve toplamlari sisirir.
/// </summary>
public static class RelationshipScoring
{
    public sealed record DatasetResult(string Dataset, List<Case> Cases, int TruePositives, int FalsePositives,
        int FalseNegatives, int TrapHits, int HighConfidence, int HighConfidenceCorrect,
        Dictionary<string, (int Found, int Total)> ByKind);

    public static DatasetResult Score(string dataset, SemanticGold gold, IReadOnlyList<RelationshipProfile> discovered)
    {
        var found = discovered
            .Where(e => e.FromColumns.Count == 1 && e.ToColumns.Count == 1)
            .GroupBy(e => Key(e.FromTable, e.FromColumns[0], e.ToTable, e.ToColumns[0]))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var goldKeys = gold.Relationships.ToDictionary(e => Key(e.From, e.To), StringComparer.Ordinal);
        var trapKeys = gold.NonRelationships.ToDictionary(e => Key(e.From, e.To), StringComparer.Ordinal);
        var cases = new List<Case>();
        var byKind = new Dictionary<string, (int Found, int Total)>(StringComparer.Ordinal);

        foreach (var (key, edge) in goldKeys)
        {
            var hit = found.TryGetValue(key, out var match);
            var (f, t) = byKind.GetValueOrDefault(edge.Kind);
            byKind[edge.Kind] = (f + (hit ? 1 : 0), t + 1);

            cases.Add(new Case
            {
                Name = $"{dataset}: {Short(edge.From)} → {Short(edge.To)}",
                Group = $"{dataset}/{edge.Kind}",
                Success = hit,
                Message = hit ? null : $"Bulunamadi ({KindLabel(edge.Kind)}){(edge.Note is null ? "" : " — " + edge.Note)}",
                Metrics = [Metric.Higher("found", hit ? 1 : 0, "ratio")],
                Details = new
                {
                    expected = new { from = edge.From, to = edge.To, edge.Kind },
                    discovered = match is null ? null : Describe(match)
                }
            });
        }

        var falsePositives = found.Where(p => !goldKeys.ContainsKey(p.Key)).ToList();
        foreach (var (key, edge) in falsePositives)
        {
            var trap = trapKeys.GetValueOrDefault(key);
            cases.Add(new Case
            {
                Name = $"{dataset}: {Short(edge.FromTable)}.{edge.FromColumns[0]} → {Short(edge.ToTable)}.{edge.ToColumns[0]}",
                Group = trap is null ? $"{dataset}/yanlis-pozitif" : $"{dataset}/tuzak",
                Success = false,
                Message = trap is null
                    ? "Altin veride olmayan bir iliski kuruldu."
                    : $"Tuzaga dusuldu: {trap.Reason}",
                Metrics = [Metric.Lower("false_positive", 1, "count")],
                Details = new { discovered = Describe(edge), trap = trap?.Reason }
            });
        }

        // Kurulmayan tuzaklar da vaka olarak listeleniyor: "tuzaga dusmedi"
        // bir basari ve trendde gorunmesi gerekiyor.
        foreach (var (key, trap) in trapKeys.Where(p => !found.ContainsKey(p.Key)))
        {
            cases.Add(new Case
            {
                Name = $"{dataset}: ✗ {Short(trap.From)} → {Short(trap.To)}",
                Group = $"{dataset}/tuzak",
                Success = true,
                Metrics = [Metric.Higher("avoided", 1, "ratio")],
                Details = new { trap = trap.Reason }
            });
        }

        var truePositives = found.Keys.Count(goldKeys.ContainsKey);
        var high = found.Values.Where(e => e.Confidence == "high").ToList();

        return new DatasetResult(
            dataset, cases, truePositives, falsePositives.Count, goldKeys.Count - truePositives,
            found.Keys.Count(trapKeys.ContainsKey),
            high.Count,
            high.Count(e => goldKeys.ContainsKey(Key(e.FromTable, e.FromColumns[0], e.ToTable, e.ToColumns[0]))),
            byKind);
    }

    /// <summary>Veri setleri uzerinden toplam (mikro ortalama) ve set basina metrikler.</summary>
    public static List<Metric> Summarize(IReadOnlyList<DatasetResult> results)
    {
        var metrics = new List<Metric>();
        var tp = results.Sum(r => r.TruePositives);
        var fp = results.Sum(r => r.FalsePositives);
        var fn = results.Sum(r => r.FalseNegatives);

        AddPrf(metrics, "", tp, fp, fn);
        metrics.Add(Metric.Lower("trap_hits", results.Sum(r => r.TrapHits), "count"));

        var high = results.Sum(r => r.HighConfidence);
        if (high > 0)
            metrics.Add(Metric.Higher("precision.high_confidence",
                results.Sum(r => r.HighConfidenceCorrect) / (double)high, "ratio"));

        foreach (var kind in results.SelectMany(r => r.ByKind.Keys).Distinct().Order(StringComparer.Ordinal))
        {
            var found = results.Sum(r => r.ByKind.GetValueOrDefault(kind).Found);
            var total = results.Sum(r => r.ByKind.GetValueOrDefault(kind).Total);
            if (total > 0) metrics.Add(Metric.Higher($"recall.{kind}", found / (double)total, "ratio"));
        }

        foreach (var result in results)
            AddPrf(metrics, "@" + result.Dataset, result.TruePositives, result.FalsePositives, result.FalseNegatives);

        return metrics;
    }

    private static void AddPrf(List<Metric> metrics, string suffix, int tp, int fp, int fn)
    {
        var precision = tp + fp == 0 ? 1 : tp / (double)(tp + fp);
        var recall = tp + fn == 0 ? 1 : tp / (double)(tp + fn);
        var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        metrics.Add(Metric.Higher("precision" + suffix, precision, "ratio"));
        metrics.Add(Metric.Higher("recall" + suffix, recall, "ratio"));
        metrics.Add(Metric.Higher("f1" + suffix, f1, "ratio"));
    }

    private static object Describe(RelationshipProfile edge) => new
    {
        from = $"{edge.FromTable}.{string.Join(",", edge.FromColumns)}",
        to = $"{edge.ToTable}.{string.Join(",", edge.ToColumns)}",
        edge.Source,
        edge.Confidence,
        edge.ValueOverlap,
        edge.NeedsConfirmation,
        edge.Cardinality
    };

    private static string KindLabel(string kind) => kind switch
    {
        "fk" => "bildirilmis FK",
        "name" => "ad eslesmesi",
        "name-role" => "rol onekli ad",
        "typo" => "yazim hatasi",
        "abbreviation" => "kisaltma",
        "language" => "farkli dil",
        "self" => "oz-referans",
        "lookup" => "kosullu lookup",
        _ => kind
    };

    /// <summary>dbo.L_INT_ExportReference.X -> L_INT_ExportReference.X (okunabilirlik).</summary>
    private static string Short(string qualified) =>
        qualified.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) ? qualified[4..] : qualified;

    public static string Key(string fromTable, string fromColumn, string toTable, string toColumn) =>
        $"{Norm(fromTable)}.{Norm(fromColumn)}>{Norm(toTable)}.{Norm(toColumn)}";

    /// <summary>"sema.tablo.kolon" iki ucu icin.</summary>
    public static string Key(string from, string to)
    {
        var (ft, fc) = Split(from);
        var (tt, tc) = Split(to);
        return Key(ft, fc, tt, tc);
    }

    private static (string Table, string Column) Split(string qualified)
    {
        var last = qualified.LastIndexOf('.');
        return (qualified[..last], qualified[(last + 1)..]);
    }

    private static string Norm(string value) => value.Replace("[", "").Replace("]", "").Trim().ToLowerInvariant();
}

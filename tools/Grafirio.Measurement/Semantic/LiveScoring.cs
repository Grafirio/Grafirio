using System.Globalization;
using System.Text.Json.Nodes;
using Grafirio.Measurement.Reporting;

namespace Grafirio.Measurement.Semantic;

/// <summary>Altin SQL'in test veritabanindaki sonucu: satir basina (etiket, deger).</summary>
public sealed record GoldRow(string? Label, double? Value);

/// <summary>
/// SQL Generation ve Visualization Accuracy — canli sistemin cevabi altin
/// SQL'in sonucuyla.
///
/// Sistemin ham SQL satirlari disari cikmiyor; kullanicinin gordugu sey grafik
/// verisi. Bu yuzden "calistirma dogrulugu" grafikteki SAYILAR uzerinden
/// olculuyor (Spider/BIRD'deki execution accuracy'nin ayni fikri): SQL ne
/// kadar farkli yazilmis olursa olsun, ayni sayilari uretiyorsa dogrudur.
/// Etiketler karsilastirmaya girmiyor — "Ocak" ile 1 ayni ay.
/// </summary>
public static class LiveScoring
{
    /// <summary>PyCaret degerleri 2 basamaga yuvarliyor; karsilastirma o hassasiyette.</summary>
    private const double AbsoluteTolerance = 0.011;
    private const double RelativeTolerance = 1e-6;

    public sealed record SqlVerdict(bool Correct, string? Problem, bool FanOutSuspected);

    public sealed record ChartVerdict(bool TypeOk, bool Valid, bool Complete, bool OrderOk, string? Problem);

    public static SqlVerdict CompareValues(IReadOnlyList<GoldRow> gold, IReadOnlyList<double?> actual, bool ordered)
    {
        var expected = gold.Select(g => g.Value).ToList();
        if (expected.Count != actual.Count)
        {
            return new SqlVerdict(false,
                $"Satir sayisi farkli: beklenen {expected.Count}, gelen {actual.Count}.", false);
        }

        var left = ordered ? expected : expected.OrderBy(v => v ?? double.MinValue).ToList();
        var right = ordered ? actual.ToList() : actual.OrderBy(v => v ?? double.MinValue).ToList();

        for (var i = 0; i < left.Count; i++)
        {
            if (Close(left[i], right[i])) continue;

            // 1:N join toplami sisirir: gelen deger beklenenin tam kati ise bu
            // bir fan-out isaretidir ve ayri raporlanmali (en tehlikeli SQL hatasi:
            // sonuc makul gorunur).
            var fanOut = left[i] is { } e && right[i] is { } a && e != 0 && a / e > 1.5
                         && Math.Abs(a / e - Math.Round(a / e)) < 1e-6;
            return new SqlVerdict(false,
                $"Deger farkli ({(ordered ? $"{i + 1}. satir" : "sirali karsilastirma")}): " +
                $"beklenen {Format(left[i])}, gelen {Format(right[i])}" +
                (fanOut ? " — tam kat: join satirlari cogaltmis olabilir (fan-out)." : "."),
                fanOut);
        }

        return new SqlVerdict(true, null, false);
    }

    public static ChartVerdict CheckChart(JsonNode? result, SemanticQuestion question, IReadOnlyList<GoldRow> gold)
    {
        var chart = result?["charts"] is JsonArray charts && charts.Count > 0 ? charts[0] as JsonObject : null;
        if (chart is null) return new ChartVerdict(false, false, false, false, "Grafik yok.");

        var type = chart["type"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : null;
        var accepted = question.Gold?.Charts ?? [];
        var typeOk = accepted.Count == 0 || (type is not null && accepted.Contains(type, StringComparer.OrdinalIgnoreCase));

        var labels = chart["data"]?["labels"] as JsonArray;
        var values = chart["data"]?["datasets"] is JsonArray datasets && datasets.Count > 0
            ? datasets[0]?["data"] as JsonArray
            : null;

        // chart_validation.py'nin kurallari: kategori grafiginde etiketler metin,
        // degerler sayi/null ve sayilari esit.
        var valid = labels is not null && values is not null && labels.Count == values.Count
                    && labels.All(l => l is JsonValue lv && lv.TryGetValue<string>(out _))
                    && values.All(v => v is null || (v is JsonValue vv && vv.TryGetValue<double>(out var d) && double.IsFinite(d)))
                    && !string.IsNullOrWhiteSpace(chart["title"]?.ToString());

        // Eksiksizlik: grafik, sorunun cevabi olan butun gruplari gosteriyor mu.
        // Limitli sorularda (ilk 5) altin SQL zaten TOP N; esitlik yeterli.
        var complete = values is not null && values.Count == gold.Count;

        // Sira: "en cok N" ve zaman serilerinde sira cevabin parcasi.
        var orderOk = true;
        if (question.Gold?.Ordered == true && values is not null)
        {
            var actual = values.Select(ToDouble).ToList();
            var expected = gold.Select(g => g.Value).ToList();
            orderOk = actual.Count == expected.Count && actual.Zip(expected).All(p => Close(p.First, p.Second));
        }

        var problems = new List<string>();
        if (!typeOk) problems.Add($"grafik turu '{type}', kabul edilenler: {string.Join(", ", accepted)}");
        if (!valid) problems.Add("grafik yapisi gecersiz (etiket/deger sayisi, bos baslik ya da sayi olmayan deger)");
        if (!complete) problems.Add($"grafikte {values?.Count ?? 0} nokta var, beklenen {gold.Count}");
        if (!orderOk) problems.Add("siralama beklenen sirayla ayni degil");

        return new ChartVerdict(typeOk, valid, complete, orderOk, problems.Count == 0 ? null : string.Join("; ", problems));
    }

    /// <summary>Grafigin ilk serisinin degerleri.</summary>
    public static List<double?> ChartValues(JsonNode? result) =>
        result?["charts"] is JsonArray charts && charts.Count > 0
        && charts[0]?["data"]?["datasets"] is JsonArray datasets && datasets.Count > 0
        && datasets[0]?["data"] is JsonArray values
            ? values.Select(ToDouble).ToList()
            : [];

    private static double? ToDouble(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;

    private static bool Close(double? expected, double? actual)
    {
        if (expected is null || actual is null) return expected is null && actual is null;
        var e = Math.Round(expected.Value, 2, MidpointRounding.AwayFromZero);
        var diff = Math.Abs(e - actual.Value);
        return diff <= AbsoluteTolerance || diff <= Math.Abs(e) * RelativeTolerance;
    }

    private static string Format(double? value) =>
        value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "null";

    /// <summary>Kosu metrikleri: SQL ve grafik ayri belgelerde.</summary>
    public static (List<Metric> Sql, List<Metric> Chart) Summarize(IReadOnlyList<(SqlVerdict Sql, ChartVerdict Chart, string Status)> items)
    {
        var sql = new List<Metric>();
        var chart = new List<Metric>();
        if (items.Count == 0) return (sql, chart);

        double Ratio(Func<(SqlVerdict Sql, ChartVerdict Chart, string Status), bool> predicate) =>
            items.Count(predicate) / (double)items.Count;

        sql.Add(Metric.Higher("execution.accuracy", Ratio(i => i.Sql.Correct), "ratio"));
        sql.Add(Metric.Higher("execution.success", Ratio(i => i.Status == "completed"), "ratio"));
        sql.Add(Metric.Lower("fan_out.suspected", items.Count(i => i.Sql.FanOutSuspected), "count"));
        sql.Add(Metric.Info("questions", items.Count, "count"));

        var completed = items.Where(i => i.Status == "completed").ToList();
        if (completed.Count > 0)
        {
            chart.Add(Metric.Higher("type.accuracy", completed.Count(i => i.Chart.TypeOk) / (double)completed.Count, "ratio"));
            chart.Add(Metric.Higher("structure.validity", completed.Count(i => i.Chart.Valid) / (double)completed.Count, "ratio"));
            chart.Add(Metric.Higher("completeness", completed.Count(i => i.Chart.Complete) / (double)completed.Count, "ratio"));
            chart.Add(Metric.Higher("order.accuracy", completed.Count(i => i.Chart.OrderOk) / (double)completed.Count, "ratio"));
            chart.Add(Metric.Higher("fidelity", completed.Count(i => i.Sql.Correct && i.Chart.Complete && i.Chart.OrderOk)
                                                / (double)completed.Count, "ratio"));
            chart.Add(Metric.Info("charts", completed.Count, "count"));
        }

        return (sql, chart);
    }
}

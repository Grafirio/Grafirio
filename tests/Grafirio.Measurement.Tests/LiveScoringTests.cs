using System.Text.Json.Nodes;
using Grafirio.Measurement.Semantic;

namespace Grafirio.Measurement.Tests;

public class LiveScoringTests
{
    private static readonly GoldRow[] Gold = [new("Delivered", 3000), new("Cancelled", 1000), new("Shipped", 1000.004)];

    [Fact]
    public void Siralamasiz_karsilastirma_yuvarlamaya_toleransli()
    {
        var verdict = LiveScoring.CompareValues(Gold, [1000, 3000, 1000], ordered: false);
        Assert.True(verdict.Correct);
    }

    [Fact]
    public void Sirali_karsilastirmada_sira_farki_hata()
    {
        Assert.False(LiveScoring.CompareValues(Gold, [1000, 3000, 1000], ordered: true).Correct);
        Assert.True(LiveScoring.CompareValues(Gold, [3000, 1000, 1000], ordered: true).Correct);
    }

    [Fact]
    public void Tam_kat_sapma_fan_out_olarak_isaretleniyor()
    {
        var verdict = LiveScoring.CompareValues([new GoldRow(null, 6000)], [18000], ordered: false);
        Assert.False(verdict.Correct);
        Assert.True(verdict.FanOutSuspected);
        Assert.Contains("fan-out", verdict.Problem);
    }

    [Fact]
    public void Satir_sayisi_farki_raporlaniyor()
    {
        var verdict = LiveScoring.CompareValues(Gold, [3000, 1000], ordered: false);
        Assert.False(verdict.Correct);
        Assert.Contains("Satir sayisi", verdict.Problem);
    }

    private static JsonNode Result(string type, string labels, string values, string title = "Durum") => JsonNode.Parse($$"""
        { "charts": [ { "type": "{{type}}", "title": "{{title}}",
          "data": { "labels": {{labels}}, "datasets": [ { "label": "Adet", "data": {{values}} } ] } } ] }
        """)!;

    private static SemanticQuestion Question(bool ordered, params string[] charts) => new()
    {
        Id = "q", Question = "q", Gold = new GoldAnswer { Sql = "x", Charts = charts.ToList(), Ordered = ordered }
    };

    [Fact]
    public void Grafik_turu_yapi_eksiksizlik_ve_sira_denetleniyor()
    {
        var good = LiveScoring.CheckChart(Result("bar", """["Delivered","Cancelled","Shipped"]""", "[3000,1000,1000]"),
            Question(true, "bar", "pie"), Gold);
        Assert.Null(good.Problem);

        var wrongType = LiveScoring.CheckChart(Result("line", """["a","b","c"]""", "[3000,1000,1000]"),
            Question(false, "bar"), Gold);
        Assert.False(wrongType.TypeOk);

        var truncated = LiveScoring.CheckChart(Result("bar", """["a","b"]""", "[3000,1000]"), Question(false, "bar"), Gold);
        Assert.False(truncated.Complete);

        var invalid = LiveScoring.CheckChart(Result("bar", """["a","b","c"]""", "[3000,1000]"), Question(false, "bar"), Gold);
        Assert.False(invalid.Valid);

        var misordered = LiveScoring.CheckChart(Result("bar", """["a","b","c"]""", "[1000,3000,1000]"), Question(true, "bar"), Gold);
        Assert.False(misordered.OrderOk);
    }

    [Fact]
    public void Ozet_sql_ve_grafik_metriklerini_ayiriyor()
    {
        var ok = new LiveScoring.SqlVerdict(true, null, false);
        var bad = new LiveScoring.SqlVerdict(false, "x", true);
        var chartOk = new LiveScoring.ChartVerdict(true, true, true, true, null);
        var chartBad = new LiveScoring.ChartVerdict(false, true, true, true, "tur");

        var (sql, chart) = LiveScoring.Summarize(
            [(ok, chartOk, "completed"), (bad, chartBad, "completed"), (bad, chartBad, "clarification")]);
        var s = sql.ToDictionary(m => m.Name);
        var c = chart.ToDictionary(m => m.Name);

        Assert.Equal(1 / 3d, s["execution.accuracy"].Value, 6);
        Assert.Equal(2 / 3d, s["execution.success"].Value, 6);
        Assert.Equal(2, s["fan_out.suspected"].Value);
        Assert.Equal(0.5, c["type.accuracy"].Value);
        Assert.Equal(0.5, c["fidelity"].Value);
    }
}

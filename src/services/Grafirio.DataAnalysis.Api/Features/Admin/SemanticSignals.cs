using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data.Mongo;

namespace Grafirio.DataAnalysis.Api.Features.Admin;

/// <summary>Bir sorunun sinyal hesabi icin gereken alanlari (kimlik ve metin disari cikmiyor).</summary>
public sealed record SignalQuestion(
    Guid Id,
    Guid ConfigId,
    string UserId,
    string Question,
    string Status,
    DateTime CreatedAt,
    Guid? ParentQueryId,
    short? FeedbackRating,
    string? ChartTypeOverride,
    string? Error);

/// <summary>Bir "Analiz Et" kaydinin sinyal icin gereken kismi.</summary>
public sealed record SignalAnalysis(string Status, DateTime CreatedAt, string ConfigJson);

/// <param name="Direction">higher | lower | none — panel iyilesme/kotulesmeyi buna gore boyuyor.</param>
public sealed record Signal(string Name, double? Value, string Unit, string Direction, string Description,
    long SampleSize);

public sealed record CapabilitySignals(string Capability, IReadOnlyList<Signal> Signals);

public sealed record DailySignal(string Date, int Questions, int Completed, int Clarifications, int Failed,
    int FeedbackUp, int FeedbackDown, int ChartOverrides);

public sealed record SemanticSignalReport(DateTime From, DateTime To, IReadOnlyList<CapabilitySignals> Capabilities,
    IReadOnlyList<DailySignal> Daily);

/// <summary>
/// Semantik zekanin URETIMDEKI dolayli sinyalleri — altin veri olmadan,
/// gercek kullanimdan. Degerlendirme setleri kaniti verir ama yalnizca test
/// semalarinda; bu sinyaller musterinin gercek semasinda ne oldugunu gosterir.
///
/// Her yetenek icin sinyal ve neden o sinyal:
///   Schema Understanding     kullanicinin sozlugu duzeltmesi (anlam/es anlamli)
///                            ve "Analiz Et"in sordugu soru sayisi
///   Relationship Discovery   analiz basina bulunan/cikarsanan iliski; onerilen
///                            eslesmelerin kabul/ret orani
///   Semantic Mapping         ayni sorunun tekrar sorulmasi, olumsuz oy
///   Unknown Data Handling    netlestirme orani ve netlestirmenin cozulme orani
///   SQL Generation           basarisiz sorgular, hata sinifina gore
///   Visualization Accuracy   kullanicinin grafik turunu degistirmesi, olumlu oy
///
/// Saf hesap: veritabani yok, test edilebilir.
/// </summary>
public static class SemanticSignals
{
    public static readonly TimeSpan ReaskWindow = TimeSpan.FromHours(24);

    public static SemanticSignalReport Compute(
        IReadOnlyList<SignalQuestion> questions,
        IReadOnlyList<SignalAnalysis> analyses,
        IReadOnlyList<(string Kind, bool Accepted, long Count)> facts,
        DateTime from, DateTime to)
    {
        var total = questions.Count;
        var completed = questions.Count(q => q.Status == "completed");
        var clarifications = questions.Where(q => q.Status == "clarification").ToList();
        var failed = questions.Where(q => q.Status == "failed").ToList();
        var rated = questions.Where(q => q.FeedbackRating is not null).ToList();

        var capabilities = new List<CapabilitySignals>
        {
            new("schema", SchemaSignals(analyses, facts)),
            new("relationships", RelationshipSignals(analyses, facts)),
            new("mapping",
            [
                Rate("reask.rate", Reasks(questions), total, "lower",
                    "Ayni kullanicinin ayni soruyu 24 saat icinde yeniden sorma orani."),
                Rate("feedback.negative.rate", rated.Count(q => q.FeedbackRating < 0), rated.Count, "lower",
                    "Oy verilen sonuclarda olumsuz oy orani."),
                Rate("completed.rate", completed, total, "higher", "Tamamlanan soru orani.")
            ]),
            new("unknown",
            [
                Rate("clarification.rate", clarifications.Count, total, "none",
                    "Sistemin soru sordugu tur orani. Yuksekligi tek basina iyi ya da kotu degil."),
                Rate("clarification.resolved.rate",
                    clarifications.Count(c => questions.Any(q => q.ParentQueryId == c.Id && q.Status == "completed")),
                    clarifications.Count, "higher",
                    "Netlestirmeden sonra kullanicinin cevabiyla tamamlanan tur orani: soru isabetli miydi.")
            ]),
            new("sql", SqlSignals(questions, failed)),
            new("visualization",
            [
                Rate("chart.override.rate", questions.Count(q => q.ChartTypeOverride is not null), completed, "lower",
                    "Tamamlanan sonuclarda kullanicinin modelin sectigi grafik turunu degistirme orani."),
                Rate("feedback.positive.rate", rated.Count(q => q.FeedbackRating > 0), rated.Count, "higher",
                    "Oy verilen sonuclarda olumlu oy orani.")
            ])
        };

        var daily = questions
            .GroupBy(q => q.CreatedAt.ToString("yyyy-MM-dd"))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new DailySignal(g.Key, g.Count(),
                g.Count(q => q.Status == "completed"), g.Count(q => q.Status == "clarification"),
                g.Count(q => q.Status == "failed"), g.Count(q => q.FeedbackRating > 0),
                g.Count(q => q.FeedbackRating < 0), g.Count(q => q.ChartTypeOverride is not null)))
            .ToList();

        return new SemanticSignalReport(from, to, capabilities, daily);
    }

    private static List<Signal> SchemaSignals(IReadOnlyList<SignalAnalysis> analyses,
        IReadOnlyList<(string Kind, bool Accepted, long Count)> facts)
    {
        var questionCounts = analyses.Select(a => ReadInt(a.ConfigJson, "generatedQuestionCount")
                                                  ?? ReadInt(a.ConfigJson, "questionCount"))
            .Where(v => v is not null).Select(v => (double)v!.Value).ToList();

        var corrections = facts.Where(f => f.Accepted && f.Kind is LearnedFact.Meaning or LearnedFact.Synonym
                                                           or LearnedFact.CodeMeaning or LearnedFact.Label)
            .Sum(f => f.Count);

        return
        [
            new("setup_questions.mean", questionCounts.Count == 0 ? null : questionCounts.Average(), "count", "lower",
                "'Analiz Et'in kullaniciya sordugu ortalama soru: modelin emin olamadigi kolon sayisinin vekili.",
                questionCounts.Count),
            new("corrections.per_analysis", analyses.Count == 0 ? null : corrections / (double)analyses.Count,
                "count", "lower",
                "Analiz basina kullanicinin ogrettigi anlam/es anlamli/kod: her biri modelin bilemedigi bir sey.",
                analyses.Count),
            Rate("analysis.success.rate", analyses.Count(a => a.Status is "ready" or "awaiting_answers"),
                analyses.Count, "higher", "Tamamlanan (hazir ya da cevap bekleyen) analiz orani.")
        ];
    }

    private static List<Signal> RelationshipSignals(IReadOnlyList<SignalAnalysis> analyses,
        IReadOnlyList<(string Kind, bool Accepted, long Count)> facts)
    {
        var ready = analyses.Where(a => a.Status is "ready" or "awaiting_answers").ToList();
        var found = ready.Select(a => ReadInt(a.ConfigJson, "profileStats", "relationshipCount"))
            .Where(v => v is not null).Select(v => (double)v!.Value).ToList();
        var inferred = ready.Select(a => ReadInt(a.ConfigJson, "profileStats", "inferredRelationshipCount"))
            .Where(v => v is not null).Select(v => (double)v!.Value).ToList();

        var accepted = facts.Where(f => f.Kind == LearnedFact.Relationship && f.Accepted).Sum(f => f.Count);
        var rejected = facts.Where(f => f.Kind == LearnedFact.Relationship && !f.Accepted).Sum(f => f.Count);

        return
        [
            new("found.per_analysis", found.Count == 0 ? null : found.Average(), "count", "none",
                "Analiz basina bulunan iliski sayisi (bildirilmis + cikarsanmis).", found.Count),
            new("inferred.per_analysis", inferred.Count == 0 ? null : inferred.Average(), "count", "none",
                "Analiz basina adlardan ve veriden CIKARSANAN iliski. Surekli sifirsa cikarim calismiyor olabilir.",
                inferred.Count),
            Rate("confirmation.acceptance.rate", accepted, accepted + rejected, "higher",
                "Kullaniciya onaylatilan eslesmelerin kabul orani: kesfin isabeti.")
        ];
    }

    private static List<Signal> SqlSignals(IReadOnlyList<SignalQuestion> questions, List<SignalQuestion> failed)
    {
        var executed = questions.Count(q => q.Status is "completed" or "failed");
        var signals = new List<Signal>
        {
            Rate("failure.rate", failed.Count, executed, "lower", "Calistirilan sorgularda basarisizlik orani.")
        };

        foreach (var group in failed.GroupBy(f => ClassifyError(f.Error)).OrderBy(g => g.Key, StringComparer.Ordinal))
            signals.Add(Rate($"failure.{group.Key}.rate", group.Count(), executed, "lower",
                $"Calistirilan sorgularda '{group.Key}' sinifindan hata orani."));

        return signals;
    }

    /// <summary>
    /// Hata mesajindan kaba sinif. Mesajlar insan icin yazilmis; sinif yalnizca
    /// trend icin, tekil teshis icin kaydin kendisine bakilmali.
    /// </summary>
    public static string ClassifyError(string? error)
    {
        var text = (error ?? "").ToLowerInvariant();
        if (text.Contains("zaman aşımı") || text.Contains("timeout") || text.Contains("timed out")
            || text.Contains("maximum duration")) return "timeout";
        if (text.Contains("not allowed") || text.Contains("izin") || text.Contains("okuma sorgu")
            || text.Contains("read-only") || text.Contains("policy") || text.Contains("schema-qualified"))
            return "policy";
        if (text.Contains("sql") || text.Contains("syntax") || text.Contains("invalid column")
            || text.Contains("invalid object")) return "sql";
        if (text.Contains("bridge")) return "bridge";
        return "other";
    }

    /// <summary>Ayni kullanici + ayni analiz + ayni (normalize) soru, 24 saat icinde.</summary>
    public static int Reasks(IReadOnlyList<SignalQuestion> questions)
    {
        var count = 0;
        foreach (var group in questions.GroupBy(q => (q.UserId, q.ConfigId, Text: Normalize(q.Question))))
        {
            DateTime? previous = null;
            foreach (var question in group.OrderBy(q => q.CreatedAt))
            {
                if (previous is not null && question.CreatedAt - previous.Value <= ReaskWindow) count++;
                previous = question.CreatedAt;
            }
        }
        return count;
    }

    private static string Normalize(string question) =>
        string.Join(' ', question.ToLowerInvariant().Split(default(char[]), StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd('?', '.', '!', ' ');

    private static Signal Rate(string name, long numerator, long denominator, string direction, string description) =>
        new(name, denominator == 0 ? null : numerator / (double)denominator, "ratio", direction, description, denominator);

    private static int? ReadInt(string json, params string[] path)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement;
            foreach (var name in path)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return null;
            }
            return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;

namespace Grafirio.DataAnalysis.Api.Data.Entities;

/// <summary>
/// LLM harcamasini kayda yazan varliklarin ortak alanlari.
///
/// Olcum sistemi (OpenTelemetry) toplu egilimi gosteriyor ama tek tek kayitlari
/// tutmuyor ve toplayici kapaliysa hic tutmuyor. "Bu soru ne kadara mal oldu",
/// "gecen ay analiz basina ortalama kac token harcandi" sorulari kaydin
/// kendisinden cevaplanabilsin diye degerler satira da yaziliyor.
///
/// Hepsi bos gecilebilir: bu alanlar eklenmeden once olusan satirlarda deger
/// yok ve sifir yazmak "hic harcama yok" demek olurdu.
/// </summary>
public interface ILlmUsageColumns
{
    int? LlmCalls { get; set; }
    int? LlmInputTokens { get; set; }
    int? LlmCachedInputTokens { get; set; }
    int? LlmOutputTokens { get; set; }
    int? LlmReasoningTokens { get; set; }
    int? LlmDurationMs { get; set; }
}

public static class LlmUsageColumnsExtensions
{
    public static void Apply(this ILlmUsageColumns row, LlmUsage usage)
    {
        row.LlmCalls = usage.Calls;
        row.LlmInputTokens = usage.InputTokens;
        row.LlmCachedInputTokens = usage.CachedInputTokens;
        row.LlmOutputTokens = usage.OutputTokens;
        row.LlmReasoningTokens = usage.ReasoningTokens;
        row.LlmDurationMs = usage.DurationMs;
    }

    /// <summary>Kayittaki harcama; hic yazilmamissa null.</summary>
    public static object? UsageResponse(this ILlmUsageColumns row) => row.LlmCalls is null
        ? null
        : new
        {
            calls = row.LlmCalls,
            inputTokens = row.LlmInputTokens,
            cachedInputTokens = row.LlmCachedInputTokens,
            outputTokens = row.LlmOutputTokens,
            reasoningTokens = row.LlmReasoningTokens,
            durationMs = row.LlmDurationMs
        };
}

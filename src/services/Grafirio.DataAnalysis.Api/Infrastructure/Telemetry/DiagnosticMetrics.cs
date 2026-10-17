namespace Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;

/// <summary>Request outcomes, not SQL execution or answer correctness. Empty results are failures.</summary>
public static class DiagnosticMetrics
{
    public static string Group(string? status) => status switch
    {
        "completed" => "completed",
        "failed" => "failed",
        "preparing" or "submitted" or "queued" or "processing" or "cancelling" => "pending",
        "clarification" => "clarification",
        "cancelled" => "cancelled",
        _ => "other"
    };

    // Preserve the existing terminal latency population, including clarification and cancellation.
    public static bool IsTerminal(string? status) => Group(status) is "completed" or "failed" or "clarification" or "cancelled";
    public static bool IsEmptyResult(string? status, string? errorCode) => status == "failed" && errorCode == "empty_result";
    public static double? SuccessRate(int completed, int failed) => completed + failed == 0
        ? null : (double)completed / (completed + failed);
}

namespace Grafirio.Shared.MassTransit.Messages.AI;

/// <summary>
/// PyCaret analiz sonucu — grafik verileri ve insights
/// </summary>
public interface IPyCaretAnalysisResponse
{
    Guid RequestId { get; }
    Guid QueryId { get; }
    string CompanyId { get; }
    DateTime ResponseTime { get; }
    bool Success { get; }
    string? Error { get; }

    /// <summary>
    /// Grafik verileri JSON — [{type, title, data: {labels, datasets}}]
    /// </summary>
    string ChartsJson { get; }

    /// <summary>
    /// Analiz bulguları JSON — [{type, title, description}]
    /// </summary>
    string InsightsJson { get; }

    /// <summary>
    /// Analiz özeti (metin)
    /// </summary>
    string Summary { get; }
}

public record PyCaretAnalysisResponse(
    Guid RequestId,
    Guid QueryId,
    string CompanyId,
    DateTime ResponseTime,
    bool Success,
    string? Error,
    string ChartsJson,
    string InsightsJson,
    string Summary
) : IPyCaretAnalysisResponse;

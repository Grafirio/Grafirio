using Grafirio.DataAnalysis.Api.Data.Entities;

namespace Grafirio.DataAnalysis.Api.Application.Interfaces;

public interface IPyCaretQueryService
{
    bool IsConfigured { get; }
    Task SubmitAsync(QueryHistory query, AnalysisConfig config, CancellationToken ct);
    Task RefreshAsync(QueryHistory query, CancellationToken ct);
    Task CancelAsync(QueryHistory query, CancellationToken ct);
}
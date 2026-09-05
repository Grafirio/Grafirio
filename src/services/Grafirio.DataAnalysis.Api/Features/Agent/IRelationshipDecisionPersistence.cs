using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

public interface IRelationshipDecisionPersistence
{
    Task<SavedConnection?> GetConnectionAsync(Guid connectionId, string companyId, CancellationToken ct);
    Task<AnalysisConfig?> GetActiveConfigAsync(Guid connectionId, string companyId, CancellationToken ct);
    Task SaveFactAsync(Guid connectionId, string companyId, LearnedFact fact, CancellationToken ct);
    Task DeleteFactAsync(Guid connectionId, string companyId, string key, CancellationToken ct);
    Task<bool> TryUpdateConfigAsync(AnalysisConfig snapshot, string dictionaryJson, bool isActive, CancellationToken ct);
}
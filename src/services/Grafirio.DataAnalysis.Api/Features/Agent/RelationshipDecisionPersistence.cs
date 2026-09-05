using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

public sealed class RelationshipDecisionPersistence(DataAnalysisDbContext db, LearnedFactStore facts)
    : IRelationshipDecisionPersistence
{
    public Task<SavedConnection?> GetConnectionAsync(Guid connectionId, string companyId, CancellationToken ct) =>
        db.SavedConnections.AsNoTracking().FirstOrDefaultAsync(connection => connection.Id == connectionId
            && connection.CompanyId == companyId && connection.IsActive, ct);

    public Task<AnalysisConfig?> GetActiveConfigAsync(Guid connectionId, string companyId, CancellationToken ct) =>
        db.AnalysisConfigs.AsNoTracking()
            .Where(config => config.ConnectionId == connectionId && config.CompanyId == companyId && config.IsActive)
            .OrderByDescending(config => config.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task SaveFactAsync(Guid connectionId, string companyId, LearnedFact fact, CancellationToken ct)
    {
        await facts.SaveAsync(connectionId, companyId, fact, ct);
        await facts.SetStatusAsync(connectionId, companyId, fact.Key, null, ct);
    }

    public async Task DeleteFactAsync(Guid connectionId, string companyId, string key, CancellationToken ct)
    {
        // A previous attempt may have deleted Mongo before losing the SQL compare-and-swap.
        await facts.DeleteAsync(connectionId, companyId, key, ct);
    }

    public async Task<bool> TryUpdateConfigAsync(
        AnalysisConfig snapshot, string dictionaryJson, bool isActive, CancellationToken ct)
    {
        // Mongo is never called while a SQL transaction/row lock is held.
        var changed = await db.AnalysisConfigs.Where(config => config.Id == snapshot.Id
                && config.ConnectionId == snapshot.ConnectionId && config.CompanyId == snapshot.CompanyId
                && config.IsActive && config.Status == snapshot.Status && config.ConfigJson == snapshot.ConfigJson
                && config.TablesJson == snapshot.TablesJson && config.UpdatedAt == snapshot.UpdatedAt
                && db.SavedConnections.Any(connection => connection.Id == snapshot.ConnectionId
                    && connection.CompanyId == snapshot.CompanyId && connection.IsActive))
            .ExecuteUpdateAsync(update => update.SetProperty(config => config.ConfigJson, dictionaryJson)
                .SetProperty(config => config.IsActive, isActive)
                .SetProperty(config => config.UpdatedAt, DateTime.UtcNow), ct);
        return changed == 1;
    }
}
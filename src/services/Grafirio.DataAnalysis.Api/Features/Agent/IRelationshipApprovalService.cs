using Grafirio.DataAnalysis.Api.Data.Mongo;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

public interface IRelationshipApprovalService
{
    Task<RelationshipApprovalResult> ApplyAsync(
        Guid connectionId, string companyId, LearnedFact fact, CancellationToken ct);

    Task<RelationshipApprovalResult> ForgetAsync(
        Guid connectionId, string companyId, string key, CancellationToken ct);
}
namespace Grafirio.DataAnalysis.Api.Features.Agent;

public sealed record RelationshipApprovalResult(bool Applied, int StatusCode, string? Error = null);
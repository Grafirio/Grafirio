using Grafirio.DataAnalysis.Api.Data.Mongo;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

public sealed record RelationshipProposal(
    string FromTable, string FromColumn, string ToTable, string ToColumn)
{
    public string Key => LearnedFact.RelationshipKey(FromTable, FromColumn, ToTable, ToColumn);
}
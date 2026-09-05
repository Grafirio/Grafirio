using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.QueryPolicy;

namespace Grafirio.DataAnalysis.Tests;

public sealed class AgentQueryTableScopeTests
{
    private const string Dictionary = """{"tables":[{"name":"dbo.Orders"},{"name":"dbo.Customers"}]}""";
    private static readonly string[] Selected = ["dbo.Orders", "dbo.Customers"];

    [Theory]
    [InlineData("""{"target_table":"dbo.Orders"}""")]
    [InlineData("""{"target_table":"dbo.Orders","joins":[{"table":"dbo.Customers"}]}""")]
    [InlineData("""{"target_table":"dbo.Orders","union":{"with":[{"table":"dbo.Customers"}]}}""")]
    [InlineData("""{"target_table":"dbo.Orders","union":[{"table":"dbo.Customers"}]}""")]
    [InlineData("""{"target_table":"","relationship_proposals":[{"fromTable":"dbo.Orders","toTable":"dbo.Customers"}]}""")]
    [InlineData("""{"target_table":"dbo.Orders","target_column":"Amount","group_by":["[dbo].[Customers].[Name]"],"filters":{"State":"active"}}""")]
    [InlineData("""{"target_table":"dbo.Orders","joins":[{"table":"dbo.Customers","as":"customer"}],"group_by":["customer.Name"]}""")]
    public void AcceptsSelectedPhysicalTableReferences(string parameters) =>
        AgentQueryTableScope.Validate(parameters, Dictionary, Selected);

    [Theory]
    [InlineData("""{"target_table":"dbo.Secret"}""")]
    [InlineData("""{"target_table":"Orders"}""")]
    [InlineData("""{"target_table":"dbo.orders"}""")]
    [InlineData("""{"target_table":"dbo.Orders","joins":[{"table":"dbo.Secret"}]}""")]
    [InlineData("""{"target_table":"dbo.Orders","union":{"with":[{"table":"dbo.Secret"}]}}""")]
    [InlineData("""{"target_table":"dbo.Orders","union":[{"table":"dbo.Customers","joins":[{"table":"dbo.Secret"}]}]}""")]
    [InlineData("""{"target_table":"","relationship_proposals":[{"fromTable":"dbo.Orders","toTable":"dbo.Secret"}]}""")]
    [InlineData("""{"target_table":"dbo.Orders","joins":[{}]}""")]
    [InlineData("""{"target_table":"dbo.Orders","joins":{}}""")]
    [InlineData("""{"target_table":"dbo.Orders","target_column":"dbo.Secret.Amount"}""")]
    [InlineData("""{"target_table":"dbo.Orders","group_by":["dbo.Secret.Name"]}""")]
    [InlineData("""{"target_table":"dbo.Orders","filters":{"dbo.Secret.State":"active"}}""")]
    [InlineData("""{"target_table":"dbo.Orders","feature_columns":["other.dbo.Orders.Amount"]}""")]
    [InlineData("""{"target_table":"dbo.Orders","target_column":"Amount; SELECT 1"}""")]
    [InlineData("""{"target_table":"dbo.Orders","having":[{"column":"dbo.Secret.Amount","op":">","value":1}]}""")]
    public void RejectsUnselectedTablesInEveryStructuredBranch(string parameters) =>
        Assert.Throws<QueryPolicyException>(() => AgentQueryTableScope.Validate(parameters, Dictionary, Selected));

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"tables":["dbo.Orders"]}""")]
    [InlineData("""{"tables":["dbo.Orders","dbo.Customers","dbo.Secret"]}""")]
    [InlineData("""{"tables":[null]}""")]
    public void RejectsDictionaryThatDoesNotMatchSelection(string dictionary) =>
        Assert.Throws<QueryPolicyException>(() => AgentQueryTableScope.Validate("""{"target_table":"dbo.Orders"}""", dictionary, Selected));
}
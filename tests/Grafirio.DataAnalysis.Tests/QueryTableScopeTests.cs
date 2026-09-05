using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.QueryPolicy;

namespace Grafirio.DataAnalysis.Tests;

public sealed class QueryTableScopeTests
{
    [Fact]
    public void SelectionIntersectionAcceptsOnlyRequestedSelectedTables()
    {
        var result = QueryTableScope.RequireSelected(["[dbo].[Orders]"], ["dbo.Orders", "dbo.Customers"]);
        Assert.Equal(new[] { "[dbo].[Orders]" }, result);
    }

    [Theory]
    [InlineData("dbo.Order")]
    [InlineData("sales.Orders")]
    [InlineData("dbo.orders")]
    [InlineData("dbo.Customers")]
    [InlineData("Orders")]
    [InlineData("other.dbo.Orders")]
    [InlineData("dbo.Orders; SELECT 1")]
    public void RejectsOutsideSelectionInsteadOfSilentlyIntersecting(string table)
    {
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.RequireSelected(
            ["dbo.Orders", table], ["dbo.Orders"]));
    }

    [Fact]
    public void EmptyOrNullSelectionAndRequestAreDenied()
    {
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.RequireSelected([], ["dbo.Orders"]));
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.RequireSelected(null, ["dbo.Orders"]));
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.RequireSelected(["dbo.Orders"], []));
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.RequireSelected(["dbo.Orders"], null));
    }

    [Fact]
    public void ConfigSelectionComparisonUsesDecodedOrdinalSets()
    {
        var result = QueryTableScope.RequireCurrentConfig(
            "[\"dbo.Orders\",\"[sales].[Order.Items]\"]",
            ["[sales].[Order.Items]", "[dbo].[Orders]", "dbo.Orders"]);
        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[\"dbo.orders\"]")]
    [InlineData("[\"dbo.Orders\",\"dbo.Customers\"]")]
    public void InvalidOrStaleConfigCannotAuthorizeTables(string json)
    {
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.RequireCurrentConfig(json, ["dbo.Orders"]));
    }

    [Fact]
    public void CatalogBootstrapDoesNotNeedSelectedCustomerTables()
    {
        ReadOnlySqlPolicy.Validate(QueryTableScope.BaseTablesSql, [], allowMetadata: true);
        Assert.Throws<QueryPolicyException>(() => ReadOnlySqlPolicy.Validate(
            "SELECT * FROM dbo.Orders", [], allowMetadata: true));
    }

    [Fact]
    public void RejectsViewOrSynonymAbsentFromRealBaseTableCatalog()
    {
        SqlTableIdentity[] baseTables = [new("dbo", "Orders")];
        QueryTableScope.RequireBaseTables(["[dbo].[Orders]"], baseTables);
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.RequireBaseTables(["dbo.OrderView"], baseTables));
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.RequireBaseTables(["dbo.OrderSynonym"], baseTables));
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.RequireBaseTables(["dbo.Orders"], []));
    }

    [Theory]
    [InlineData("sys.tables")]
    [InlineData("INFORMATION_SCHEMA.COLUMNS")]
    [InlineData("dbo.Orders AS o")]
    [InlineData("")]
    public void CatalogsAndNonIdentifiersCannotBecomeSelections(string name)
    {
        Assert.Throws<QueryPolicyException>(() => QueryTableScope.Normalize([name]));
    }

    [Fact]
    public void QuotedDotsAndClosingBracketsRemainExactIdentities()
    {
        QueryTableScope.RequireBaseTables(["[sales.region].[Order]]Items]"],
            [new SqlTableIdentity("sales.region", "Order]Items")]);
    }
}
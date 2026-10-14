using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Features.Agent;

namespace Grafirio.DataAnalysis.Tests;

public class MetricRuleTests
{
    private static JsonObject Dictionary() => JsonNode.Parse("""
        {"columns":[{"table":"dbo.Orders","column":"Amount","role":"measure","dataType":"decimal"},
        {"table":"dbo.Orders","column":"Date","role":"date","dataType":"datetime"},
        {"table":"dbo.Orders","column":"Id","role":"identifier","dataType":"int"}]}
        """)!.AsObject();
    private static MetricRuleEndpoints.MetricRule Rule() => new("dbo.Orders", "Amount", "Ciro", "TL", ["sum"], "Date", true);

    [Fact]
    public void ValidRulesCanonicalizeAndRecordUserSource()
    {
        var rules = MetricRuleEndpoints.ValidateRules(Dictionary(), [Rule() with { Column = "amount" }]);
        Assert.Equal("Amount", rules[0]!["column"]!.ToString());
        Assert.Equal("user", rules[0]!["source"]!.ToString());
        Assert.Equal("reject", rules[0]!["missingPeriodPolicy"]!.ToString());
    }

    [Fact]
    public void InvalidMetricsCannotBecomeRules()
    {
        foreach (var rule in new[] { Rule() with { Table = "dbo.Other" }, Rule() with { Column = "Id" },
            Rule() with { Unit = "" }, Rule() with { AllowedAggregations = ["sql"] },
            Rule() with { DateColumn = "Amount" }, Rule() with { MaxHorizon = 37 }, Rule() with { MinHistoryPeriods = 3 } })
            Assert.Throws<ArgumentException>(() => MetricRuleEndpoints.ValidateRules(Dictionary(), [rule]));
        Assert.Throws<ArgumentException>(() => MetricRuleEndpoints.ValidateRules(Dictionary(), [Rule(), Rule()]));
    }
}

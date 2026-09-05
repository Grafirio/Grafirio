using System.Text.Json;
using System.Text.Json.Serialization;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.DataAnalysis.Tests;

public class ProfilePrivacyTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Theory]
    [InlineData("Email", "varchar", true, "Denied")]
    [InlineData("Password", "varchar", true, "Allowed")]
    [InlineData("Notes", "varchar", false, "NeedsConsent")]
    [InlineData("Amount", "decimal", false, "Allowed")]
    public void PromptNeverLeaksDeniedOrNonconsensualValues(string name, string type, bool consent, string decision)
    {
        var profile = new DatabaseProfile
        {
            SamplingConsentGiven = consent,
            Tables = [new TableProfile
            {
                Schema = "dbo", TableName = "Data",
                Columns = [new ColumnProfile
                {
                    ColumnName = name, DataType = type, SamplingDecision = decision,
                    MinValue = "private-min", MaxValue = "private-max", SampleValues = ["private-sample"]
                }]
            }]
        };
        var json = PromptProfile.Serialize(profile, Options);
        Assert.DoesNotContain("private-", json);
        Assert.DoesNotContain("minValue", json);
        Assert.DoesNotContain("maxValue", json);
        Assert.DoesNotContain("sampleValues", json);
    }

    [Fact]
    public void ValuePatternsProtectMinMaxAndSamples()
    {
        var profile = new DatabaseProfile
        {
            SamplingConsentGiven = true,
            Tables = [new TableProfile
            {
                Schema = "dbo", TableName = "Data",
                Columns = [new ColumnProfile
                {
                    ColumnName = "Category", DataType = "varchar", SamplingDecision = "Allowed",
                    MinValue = "private@example.test", MaxValue = "private@example.test",
                    SampleValues = ["private@example.test", "safe"]
                }]
            }]
        };
        var json = PromptProfile.Serialize(profile, Options);
        Assert.DoesNotContain("private@example.test", json);
        Assert.Contains("safe", json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfilerUsesOnlyApprovedColumnProjection(bool consent)
    {
        var session = new FakeDataSourceSession()
            .Respond("sys.partitions", FakeDataSourceSession.Row(("Count", 10L)))
            .Respond("INFORMATION_SCHEMA.COLUMNS",
                FakeDataSourceSession.Row(("ColumnName", "Amount"), ("DataType", "decimal")),
                FakeDataSourceSession.Row(("ColumnName", "Email"), ("DataType", "varchar")),
                FakeDataSourceSession.Row(("ColumnName", "Notes"), ("DataType", "varchar")))
            .Respond("INFORMATION_SCHEMA.TABLE_CONSTRAINTS")
            .Respond("sys.foreign_keys")
            .Respond("sys.indexes")
            .Respond("SELECT TOP", FakeDataSourceSession.Row(("Amount", 12m), ("Notes", "Category")));
        var profiler = new SchemaProfiler(NullLogger<SchemaProfiler>.Instance,
            new RelationshipDiscovery(NullLogger<RelationshipDiscovery>.Instance));
        var profile = await profiler.ProfileAsync(session, "Test", ["dbo.Data"], consent);

        Assert.Null(profile.Tables[0].Error);
        var reads = session.ExecutedQueries.Where(query => query.Contains("SELECT TOP", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!consent)
        {
            Assert.Empty(reads);
            Assert.All(profile.Tables[0].Columns, column =>
            {
                Assert.Empty(column.SampleValues);
                Assert.Null(column.MinValue);
                Assert.Null(column.MaxValue);
                Assert.False(column.StatsFromSample);
            });
        }
        else
        {
            var read = Assert.Single(reads);
            Assert.Contains("[Amount], [Notes]", read);
            Assert.DoesNotContain("[Email]", read);
            Assert.DoesNotContain("*", read);
            var email = profile.Tables[0].Columns.Single(column => column.ColumnName == "Email");
            Assert.Null(email.MinValue);
            Assert.Null(email.MaxValue);
            Assert.Empty(email.SampleValues);
        }
    }
}
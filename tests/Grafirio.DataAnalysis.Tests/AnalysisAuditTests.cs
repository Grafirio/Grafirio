using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.DataAnalysis.Api.Services;

namespace Grafirio.DataAnalysis.Tests;
public class AnalysisAuditTests
{
    private static DatabaseProfile Profile(int columns = 2) => new()
    {
        Tables = [new TableProfile
        {
            Schema = "dbo", TableName = "Orders",
            Columns = Enumerable.Range(1, columns).Select(index => new ColumnProfile
            {
                ColumnName = $"C{index}", DataType = "int", IsPrimaryKey = index == 1, IsNullable = index != 1
            }).ToList()
        }]
    };

    private static string Dictionary(int questions = 2)
    {
        var source = new JsonObject
        {
            ["tables"] = new JsonArray(), ["columns"] = new JsonArray(),
            ["questions"] = new JsonArray(Enumerable.Range(1, questions).Select(index => (JsonNode)new JsonObject
            {
                ["id"] = $"q{index}", ["table"] = "dbo.Orders", ["column"] = $"C{index}", ["question"] = "Meaning?"
            }).ToArray())
        };
        return CanonicalSchemaDictionary.Build(source.ToJsonString(), Profile(Math.Max(questions, 2))).ToJsonString();
    }

    [Fact]
    public void MissingModelColumnsRemainPresentWithUnknownMeaning()
    {
        var result = JsonNode.Parse(Dictionary(0))!;
        Assert.Single(result["tables"]!.AsArray());
        Assert.Equal(2, result["columns"]!.AsArray().Count);
        Assert.Equal("unknown", result["columns"]![0]!["meaning"]!.GetValue<string>());
        Assert.True(result["columns"]![0]!["isPrimaryKey"]!.GetValue<bool>());
        Assert.Equal("int", result["columns"]![0]!["dataType"]!.GetValue<string>());
    }

    [Fact]
    public void ModelCannotReplaceTechnicalFactsOrAddValues()
    {
        const string json = """
            {"tables":[{"name":"dbo.Orders","approximateRowCount":999}],
             "columns":[{"table":"dbo.Orders","column":"C1","dataType":"varchar","isPrimaryKey":false,
             "meaning":"Identifier","role":"identifier","sampleValues":["secret"],"minValue":"secret"}],"questions":[]}
            """;
        var root = CanonicalSchemaDictionary.Build(json, Profile());
        Assert.Equal("int", root["columns"]![0]!["dataType"]!.GetValue<string>());
        Assert.True(root["columns"]![0]!["isPrimaryKey"]!.GetValue<bool>());
        Assert.DoesNotContain("secret", root.ToJsonString());
    }

    [Theory]
    [InlineData("""{"tables":[{"name":"dbo.Unknown"}],"columns":[],"questions":[]}""")]
    [InlineData("""{"tables":[],"columns":[{"table":"dbo.Orders","column":"Unknown"}],"questions":[]}""")]
    [InlineData("""{"tables":[],"columns":[],"questions":[{"table":"dbo.Orders","column":"Unknown","question":"?"}]}""")]
    [InlineData("""{"tables":[],"columns":[{"table":"dbo.Orders","column":"C1","role":"execute"}],"questions":[]}""")]
    public void UnknownReferencesAndInvalidRolesAreRejected(string json) =>
        Assert.Throws<JsonException>(() => CanonicalSchemaDictionary.Build(json, Profile()));

    [Fact]
    public void MissingLiveTableMetadataCannotBeReady()
    {
        var profile = Profile();
        profile.Tables.Add(new TableProfile { Schema = "dbo", TableName = "Missing", Error = "Denied" });
        Assert.Throws<InvalidOperationException>(() => CanonicalSchemaDictionary.Build(Dictionary(0), profile));
    }

    [Fact]
    public void EmptyAnswersNeverCompleteAnalysis() =>
        Assert.Throws<ArgumentException>(() => AnalysisAnswers.Apply(Dictionary(), new Dictionary<string, string>(), out _, out _));

    [Theory]
    [InlineData("q1", " ")]
    [InlineData("unknown", "Meaning")]
    public void InvalidAnswersRejectWholeSubmission(string id, string value) =>
        Assert.Throws<ArgumentException>(() => AnalysisAnswers.Apply(Dictionary(),
            new Dictionary<string, string> { [id] = value }, out _, out _));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"technicalSchemaComplete":true,"tables":[],"columns":[],"questions":[]}""")]
    public void BadDictionaryCannotAppearReady(string json) => Assert.ThrowsAny<JsonException>(() => AnalysisAnswers.CountPending(json));

    [Fact]
    public void PartialAnswersRetainTheRemainingQuestion()
    {
        var updated = AnalysisAnswers.Apply(Dictionary(), new Dictionary<string, string> { ["q1"] = "Invoice id" },
            out var learned, out var remaining);
        Assert.Equal(1, remaining);
        Assert.Equal(1, AnalysisAnswers.CountPending(updated));
        Assert.Single(learned);
        Assert.Equal("q2", JsonNode.Parse(updated)!["questions"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void OnlyPersistedDisplayedQuestionsAreRequired()
    {
        var json = Dictionary(12);
        var root = JsonNode.Parse(json)!;
        Assert.Equal(12, root["generatedQuestionCount"]!.GetValue<int>());
        Assert.Equal(8, AnalysisAnswers.CountPending(json));
        var answers = root["questions"]!.AsArray().ToDictionary(question => question!["id"]!.GetValue<string>(), _ => "Meaning");
        var updated = AnalysisAnswers.Apply(json, answers, out _, out var remaining);
        Assert.Equal(0, remaining);
        Assert.Equal(0, AnalysisAnswers.CountPending(updated));
    }

    [Fact]
    public void CountDoesNotSilentlyCapPersistedQuestions()
    {
        var root = JsonNode.Parse(Dictionary())!.AsObject();
        var questions = root["questions"]!.AsArray();
        for (var index = 3; index <= 12; index++)
        {
            var question = questions[0]!.DeepClone();
            question["id"] = $"q{index}";
            questions.Add(question);
        }
        Assert.Equal(12, AnalysisAnswers.CountPending(root.ToJsonString()));
    }

    [Theory]
    [InlineData("broken", false)]
    [InlineData("[]", false)]
    [InlineData("[\"dbo.Other\"]", false)]
    [InlineData("[\"dbo.Orders\",\"dbo.Orders\"]", false)]
    [InlineData("[\"DBO.ORDERS\"]", true)]
    public void StaleSelectionIsRejected(string snapshot, bool expected) =>
        Assert.Equal(expected, AnalysisJobGuard.MatchesSelection(snapshot, ["dbo.Orders"]));

    [Theory]
    [InlineData(false, true, "analyzing", "company", "company", false)]
    [InlineData(true, false, "analyzing", "company", "company", false)]
    [InlineData(true, true, "ready", "company", "company", false)]
    [InlineData(true, true, "analyzing", "other", "company", false)]
    [InlineData(true, true, "analyzing", "company", "other", false)]
    [InlineData(true, true, "analyzing", "company", "company", true)]
    public void ConsumerRejectsInactiveOrIncorrectlyScopedJobs(bool configActive, bool connectionActive, string status,
        string configCompany, string connectionCompany, bool expected)
    {
        var id = Guid.NewGuid();
        Assert.Equal(expected, AnalysisJobGuard.CanRun(id, configCompany, configActive, status,
            id, connectionCompany, connectionActive, id, "company"));
        Assert.False(AnalysisJobGuard.CanRun(Guid.NewGuid(), "company", true, "analyzing", id, "company", true, id, "company"));
    }

    [Fact]
    public void SingleMalformedChunkIsNotPassedThrough() =>
        Assert.Throws<InvalidOperationException>(() => SchemaDictionaryMerge.Combine(["{}"]));
}
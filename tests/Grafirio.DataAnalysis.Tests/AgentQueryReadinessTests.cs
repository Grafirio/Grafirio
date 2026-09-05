using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Application.Interfaces;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class AgentQueryReadinessTests
{
    private const string ReadyDictionary = """
        {"technicalSchemaComplete":true,
         "tables":[{"name":"dbo.Orders"},{"name":"dbo.Customers"}],
         "columns":[{"table":"dbo.Orders","column":"Id"},{"table":"dbo.Customers","column":"Id"}],
         "questions":[]}
        """;

    public static IEnumerable<object[]> InvalidDictionaries()
    {
        yield return ["legacy", "{}"];
        yield return ["malformed-json", "not-json"];
        yield return ["non-object", "[]"];
        yield return ["null-root", "null"];
        foreach (var flag in new[] { "missing", "false", "null", "\"true\"" })
        {
            var root = JsonNode.Parse(ReadyDictionary)!.AsObject();
            if (flag == "missing") root.Remove("technicalSchemaComplete");
            else root["technicalSchemaComplete"] = JsonNode.Parse(flag);
            yield return [$"technical-schema-{flag}", root.ToJsonString()];
        }
        foreach (var field in new[] { "tables", "columns", "questions" })
        {
            var root = JsonNode.Parse(ReadyDictionary)!.AsObject();
            root.Remove(field);
            yield return [$"missing-{field}", root.ToJsonString()];
        }
        var pending = JsonNode.Parse(ReadyDictionary)!.AsObject();
        pending["questions"] = JsonNode.Parse("""
            [{"id":"q1","table":"dbo.Orders","column":"Id","question":"What does this identify?"}]
            """);
        Assert.Equal(1, AnalysisAnswers.CountPending(pending.ToJsonString()));
        yield return ["ready-but-pending", pending.ToJsonString()];
    }

    [Theory]
    [MemberData(nameof(InvalidDictionaries))]
    public async Task ReadyConfigWithInvalidOrPendingDictionaryRejectsBeforeLlmAndPython(string scenario, string dictionary)
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        harness.Config.ConfigJson = dictionary;
        await harness.Db.SaveChangesAsync();
        var model = new Mock<ILlmClient>(MockBehavior.Strict);
        model.SetupGet(value => value.IsConfigured).Returns(true);
        var worker = new Mock<IPyCaretQueryService>(MockBehavior.Strict);
        worker.SetupGet(value => value.IsConfigured).Returns(true);

        var result = await SubmitAsync(harness, model.Object, worker.Object);

        Assert.True(((IStatusCodeHttpResult)result).StatusCode == StatusCodes.Status409Conflict, scenario);
        var payload = JsonSerializer.SerializeToNode(((IValueHttpResult)result).Value)!;
        Assert.False(string.IsNullOrWhiteSpace(payload["error"]!.GetValue<string>()));
        Assert.Empty(await harness.Db.QueryHistories.AsNoTracking().ToListAsync());
        var stored = await harness.Db.AnalysisConfigs.AsNoTracking().SingleAsync();
        Assert.Equal("ready", stored.Status);
        Assert.Equal(dictionary, stored.ConfigJson);
        harness.VerifySelectionRead();
        model.VerifyGet(value => value.IsConfigured, Times.Once);
        model.VerifyNoOtherCalls();
        worker.VerifyGet(value => value.IsConfigured, Times.Once);
        worker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteDictionaryWithoutQuestionsReachesTranslation()
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        harness.Config.ConfigJson = ReadyDictionary;
        await harness.Db.SaveChangesAsync();
        Assert.Equal(0, AnalysisAnswers.CountPending(ReadyDictionary));
        var model = new Mock<ILlmClient>(MockBehavior.Strict);
        model.SetupGet(value => value.IsConfigured).Returns(true);
        // Stop at the translation boundary without needing a running worker.
        model.Setup(value => value.GenerateAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>())).ReturnsAsync("not-json");
        var worker = new Mock<IPyCaretQueryService>(MockBehavior.Strict);
        worker.SetupGet(value => value.IsConfigured).Returns(true);

        var result = await SubmitAsync(harness, model.Object, worker.Object);

        Assert.Equal(StatusCodes.Status502BadGateway, ((IStatusCodeHttpResult)result).StatusCode);
        model.Verify(value => value.GenerateAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.VerifySelectionRead();
        Assert.Empty(await harness.Db.QueryHistories.AsNoTracking().ToListAsync());
        worker.VerifyGet(value => value.IsConfigured, Times.Once);
        worker.VerifyNoOtherCalls();
    }

    private static Task<IResult> SubmitAsync(OwnedAnalysisEndpointHarness harness, ILlmClient model,
        IPyCaretQueryService worker)
    {
        var logger = NullLogger<LlmAnalysisService>.Instance;
        var method = typeof(AgentQueryEndpoints).GetMethod("SubmitQuery", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Task<IResult>)method.Invoke(null,
            [new AgentQueryEndpoints.QueryRequest(harness.Connection.Id, "How many orders exist?"), harness.Db,
                new LlmAnalysisService(model, logger), worker, harness.Profiles, harness.Identity.Object,
                logger, CancellationToken.None])!;
    }
}
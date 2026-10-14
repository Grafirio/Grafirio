using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Application.Interfaces;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Features.Admin;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.QueryPolicy;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class DiagnosticTests
{
    private const string Dictionary = """
        {"technicalSchemaComplete":true,"tables":[{"name":"dbo.Orders"},{"name":"dbo.Customers"}],
         "columns":[{"table":"dbo.Orders","column":"Id"},{"table":"dbo.Customers","column":"Id"}],"questions":[]}
        """;
    private static DataAnalysisDbContext Database() => new(new DbContextOptionsBuilder<DataAnalysisDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Mock<IIdentityService> Admin(bool allowed = true)
    {
        var identity = new Mock<IIdentityService>(MockBehavior.Strict);
        identity.Setup(x => x.HasBusinessRole(SemanticSignalEndpoints.PlatformAdminRole)).Returns(allowed);
        identity.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        return identity;
    }
    private static JsonNode Body(IResult result) => JsonSerializer.SerializeToNode(((IValueHttpResult)result).Value,
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static Mock<ILlmClient> Model(string response)
    {
        var model = new Mock<ILlmClient>(MockBehavior.Strict);
        model.SetupGet(x => x.IsConfigured).Returns(true);
        model.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
        return model;
    }
    private static Task<IResult> Submit(OwnedAnalysisEndpointHarness harness, ILlmClient model, IPyCaretQueryService worker, string question = "Ciroyu hesapla")
        => (Task<IResult>)typeof(AgentQueryEndpoints).GetMethod("SubmitQuery", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [new AgentQueryEndpoints.QueryRequest(harness.Connection.Id, question), harness.Db,
                new LlmAnalysisService(model, NullLogger<LlmAnalysisService>.Instance), worker, harness.Profiles,
                harness.Identity.Object, NullLogger<LlmAnalysisService>.Instance, CancellationToken.None])!;

    [Theory]
    [InlineData("not-json", "translation", "translation_failed")]
    [InlineData("""{"target_table":"dbo.Orders","group_by":"Id"}""", "validation", "invalid_parameters")]
    public async Task EarlyFailuresArePersistedWithoutGrantingSqlAuthority(string response, string stage, string code)
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        harness.Config.ConfigJson = Dictionary;
        await harness.Db.SaveChangesAsync();
        var worker = new Mock<IPyCaretQueryService>(MockBehavior.Strict);
        worker.SetupGet(x => x.IsConfigured).Returns(true);
        await Submit(harness, Model(response).Object, worker.Object);
        var diagnostic = await harness.Db.QueryDiagnostics.AsNoTracking().SingleAsync();
        Assert.Equal("failed", diagnostic.Status);
        Assert.Equal(stage, diagnostic.Stage);
        Assert.Equal(code, diagnostic.ErrorCode);
        Assert.NotNull(diagnostic.CompletedAt);
        Assert.Null(diagnostic.QueryId);
        Assert.Empty(harness.Db.QueryHistories);
        Assert.Equal(response, QueryDiagnostics.Parse(diagnostic.ContextJson)!["translationResponse"]!.GetValue<string>());
        Assert.Equal(64, diagnostic.DictionaryHash.Length);
        Assert.Equal(64, diagnostic.ContextHash.Length);
        worker.VerifyGet(x => x.IsConfigured, Times.Once);
        worker.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForeignConnectionDoesNotCreateDetailedDiagnostics()
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        harness.Config.ConfigJson = Dictionary;
        harness.Identity.SetupGet(x => x.CurrentCompanyId).Returns(Guid.NewGuid());
        await harness.Db.SaveChangesAsync();
        var worker = new Mock<IPyCaretQueryService>(MockBehavior.Strict);
        worker.SetupGet(x => x.IsConfigured).Returns(true);
        await Submit(harness, Model("{}").Object, worker.Object);
        Assert.Empty(harness.Db.QueryDiagnostics);
        Assert.Empty(harness.Db.QueryHistories);
    }

    [Fact]
    public async Task AllAdminEndpointsDenyCompanyUsersBeforeReadingOrChangingData()
    {
        var identity = Admin(false).Object;
        var id = Guid.NewGuid();
        var ct = CancellationToken.None;
        var results = new[]
        {
            await DiagnosticEndpoints.List(null!, identity, ct), await DiagnosticEndpoints.Summary(null!, identity, ct),
            await DiagnosticEndpoints.Detail(id, null!, identity, ct),
            await DiagnosticEndpoints.Review(id, new("fixed", "note"), null!, identity, ct),
            await DiagnosticEndpoints.GetSettings(null!, identity, ct),
            await DiagnosticEndpoints.PutSettings(new(90, 3, 60), null!, identity, ct),
            await DiagnosticEndpoints.AdminEvents(null!, identity, ct), await DiagnosticEndpoints.Tests(null!, identity, ct),
            await DiagnosticEndpoints.CreateTest(new(id, "test", JsonSerializer.SerializeToElement(new { target_table = "dbo.Orders" })), null!, identity, ct),
            await DiagnosticEndpoints.RunTest(id, null!, identity, null!, ct),
            await DiagnosticEndpoints.UpdateTest(id, new("test", JsonSerializer.SerializeToElement(new { target_table = "dbo.Orders" })), null!, identity, ct),
            await DiagnosticEndpoints.DeleteTest(id, null!, identity, ct)
        };
        Assert.All(results, result => Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result));
    }

    [Fact]
    public async Task OverviewUsesWorkerResultsAndComparesOnlyMatchingContexts()
    {
        await using var db = Database();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var connection = Guid.NewGuid();
        for (var i = 0; i < ids.Length; i++)
        {
            var queryId = Guid.NewGuid();
            db.QueryDiagnostics.Add(new QueryDiagnostic { Id = ids[i], QueryId = queryId, ConnectionId = connection,
                CompanyId = "company", CreatedAt = DateTime.UtcNow, ContextHash = i < 2 ? "same" : "different" + i,
                Question = "Ciro", Status = "submitted", Model = "model" });
            db.QueryHistories.Add(new QueryHistory { Id = queryId, Status = i % 2 == 0 ? "completed" : "failed",
                CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
                ResultJson = i % 2 == 0 ? "{}" : """{"error":"Sorguya uyan kayıt bulunamadı.","audit":{"executedSql":"SELECT Amount FROM dbo.Orders"}}""" });
        }
        await db.SaveChangesAsync();
        var summary = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default));
        Assert.Equal(4, summary["total"]!.GetValue<int>());
        Assert.Equal(2, summary["emptyResults"]!.GetValue<int>());
        Assert.Single(summary["inconsistent"]!.AsArray());
        Assert.Equal(ids.Take(2).ToHashSet(), summary["inconsistent"]![0]!["ids"]!.AsArray().Select(n => n!.GetValue<Guid>()).ToHashSet());
        var detail = Body(await DiagnosticEndpoints.Detail(ids[1], db, Admin().Object, default));
        Assert.Equal("failed", detail["status"]!.GetValue<string>());
        Assert.Equal("empty_result", detail["errorCode"]!.GetValue<string>());
        Assert.Contains("SELECT Amount", detail["audit"]!["executedSql"]!.GetValue<string>());
    }

    [Fact]
    public async Task OldQueryHistoryIsAvailableWithExplicitMissingContext()
    {
        await using var db = Database();
        var config = new AnalysisConfig { Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid(), CompanyId = "company" };
        var query = new QueryHistory { Id = Guid.NewGuid(), ConfigId = config.Id, CreatedAt = DateTime.UtcNow,
            Status = "failed", Question = "user@example.com", ResultJson = """{"error":"Sorguya uyan kayıt bulunamadı."}""" };
        db.AddRange(config, query); await db.SaveChangesAsync();
        var body = Body(await DiagnosticEndpoints.Detail(query.Id, db, Admin().Object, default));
        Assert.True(body["legacy"]!.GetValue<bool>());
        Assert.Equal("[gizlendi]", body["question"]!.GetValue<string>());
        var list = Body(await DiagnosticEndpoints.List(db, Admin().Object, default));
        Assert.Equal(1, list["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task ReviewAndSettingsChangesHaveAnAdminAuditTrail()
    {
        await using var db = Database();
        var row = new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        db.Add(row); await db.SaveChangesAsync();
        var identity = Admin().Object;
        await DiagnosticEndpoints.Review(row.Id, new("investigating", "user@example.com kontrol edildi"), db, identity, default);
        Assert.Equal("investigating", row.ReviewStatus);
        Assert.DoesNotContain("user@example.com", row.ReviewNote);
        await DiagnosticEndpoints.PutSettings(new(30, 4, 20), db, identity, default);
        Assert.Equal(30, (await db.DiagnosticSettings.SingleAsync()).RetentionDays);
        Assert.Equal(2, await db.DiagnosticAdminEvents.CountAsync());
        var invalid = await DiagnosticEndpoints.PutSettings(new(0, 4, 20), db, identity, default);
        Assert.Equal(400, ((IStatusCodeHttpResult)invalid).StatusCode);
        Assert.Equal(30, (await db.DiagnosticSettings.SingleAsync()).RetentionDays);
    }

    [Fact]
    public async Task SavedTestUsesOriginalDateAndChecksMeaningWithoutExecutingSql()
    {
        await using var db = Database();
        var source = new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, Question = "Bu yıl ciro",
            ContextJson = JsonSerializer.Serialize(new { dictionary = Dictionary, schema = "", conversation = "",
                date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), tables = new[] { "dbo.Orders", "dbo.Customers" } }) };
        db.Add(source); await db.SaveChangesAsync();
        var identity = Admin().Object;
        await DiagnosticEndpoints.CreateTest(new(source.Id, "Ciro", JsonSerializer.SerializeToElement(new { target_table = "dbo.Orders" })), db, identity, default);
        var test = await db.DiagnosticTestCases.SingleAsync();
        var model = Model("""{"target_table":"dbo.Orders","description":"başka açıklama"}""");
        var result = Body(await DiagnosticEndpoints.RunTest(test.Id, db, identity, new LlmAnalysisService(model.Object, NullLogger<LlmAnalysisService>.Instance), default));
        Assert.True(result["passed"]!.GetValue<bool>());
        model.Verify(x => x.GenerateAsync(It.Is<string>(s => s.Contains("2024-01-01")), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(db.QueryHistories);
        Assert.Single(QueryDiagnostics.Parse(test.RunsJson)!.AsArray());
    }

    [Fact]
    public void MaskingPreservesStructureAndRemovesCredentials()
    {
        var json = QueryDiagnostics.SafeJson("""{"filters":{"Email":"user@example.com","CustomerId":"12345678901"},"apiKey":"super-secret","rows":24}""");
        Assert.DoesNotContain("super-secret", json);
        Assert.DoesNotContain("user@example.com", json);
        Assert.DoesNotContain("12345678901", json);
        Assert.Equal(24, QueryDiagnostics.Parse(json)!["rows"]!.GetValue<int>());
        Assert.DoesNotContain("super-secret", QueryDiagnostics.SafeText("password=super-secret user@example.com"));
    }

    [Fact]
    public void InvalidParameterShapeNamesTheBrokenField()
    {
        var error = Assert.Throws<QueryPolicyException>(() => AgentQueryTableScope.Validate(
            """{"target_table":"dbo.Orders","group_by":"Id"}""", Dictionary, ["dbo.Orders", "dbo.Customers"]));
        Assert.Contains("group_by", error.Message);
        Assert.Equal("invalid_parameters", QueryDiagnostics.Classify(error.Message));
    }

    [Fact]
    public async Task TrackingNumberSearchFindsRecordsOutsideSelectedPeriod()
    {
        await using var db = Database();
        var row = new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddDays(-60), Status = "failed", Question = "Ciro" };
        db.Add(row); await db.SaveChangesAsync();
        Assert.Equal(0, Body(await DiagnosticEndpoints.List(db, Admin().Object, default))["total"]!.GetValue<int>());
        var found = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, search: row.Id.ToString()));
        Assert.Equal(1, found["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task TestExpectationsCanBeUpdatedAndDeletedWithAudit()
    {
        await using var db = Database();
        var test = new DiagnosticTestCase { Id = Guid.NewGuid(), Name = "Original", CreatedAt = DateTime.UtcNow, ExpectedJson = "{}" };
        db.Add(test); await db.SaveChangesAsync();
        var identity = Admin().Object;
        await DiagnosticEndpoints.UpdateTest(test.Id, new("Updated", JsonSerializer.SerializeToElement(new { target_table = "dbo.Orders" })), db, identity, default);
        Assert.Equal("Updated", test.Name);
        Assert.Contains("dbo.Orders", test.ExpectedJson);
        await DiagnosticEndpoints.DeleteTest(test.Id, db, identity, default);
        Assert.Empty(db.DiagnosticTestCases);
        Assert.Equal(new[] { "test_deleted", "test_updated" }, await db.DiagnosticAdminEvents.OrderBy(e => e.Action).Select(e => e.Action).ToArrayAsync());
    }
}

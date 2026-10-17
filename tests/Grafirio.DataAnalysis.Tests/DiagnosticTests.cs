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
    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 11)]
    [InlineData(3, 0)]
    public async Task CombinedResponseSharesTheWholeFilteredPopulationAndPreservesPaging(int page, int expectedItems)
    {
        await using var db = Database();
        var connection = Guid.NewGuid();
        db.QueryDiagnostics.AddRange(Enumerable.Range(0, 61).Select(i => new QueryDiagnostic {
            Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddMinutes(-i), CompanyId = "company",
            ConnectionId = connection, Status = "failed", ErrorCode = "empty_result", Question = "Ciro", DurationMs = 100 }));
        db.Add(new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
            CompanyId = "other", ConnectionId = connection, Status = "completed" });
        await db.SaveChangesAsync();
        var combined = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, days: 7, page: page,
            companyId: "company", connectionId: connection, status: "failed", errorCode: "empty_result", search: "Ciro", includeSummary: true));
        Assert.Equal(expectedItems, combined["items"]!.AsArray().Count);
        Assert.Equal(61, combined["total"]!.GetValue<int>());
        Assert.Equal(61, combined["summary"]!["total"]!.GetValue<int>());
        Assert.Equal(61, combined["summary"]!["failed"]!.GetValue<int>());
        Assert.Equal(61, combined["summary"]!["emptyResults"]!.GetValue<int>());
        Assert.Equal(100, combined["summary"]!["p95Ms"]!.GetValue<int>());
        Assert.Equal(0d, combined["summary"]!["successRate"]!.GetValue<double>());
        Assert.Equal(page, combined["page"]!.GetValue<int>());
        Assert.Equal(50, combined["pageSize"]!.GetValue<int>());
        Assert.True(combined["capturedAt"]!.GetValue<DateTime>() <= DateTime.UtcNow);
        var oldSummary = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default, days: 7,
            companyId: "company", connectionId: connection, status: "failed", errorCode: "empty_result", search: "Ciro"));
        // Separate HTTP reads have different reference timestamps, but the same stable data/metrics.
        var combinedSummary = combined["summary"]!.DeepClone();
        oldSummary.AsObject().Remove("comparison");
        combinedSummary.AsObject().Remove("comparison");
        Assert.True(JsonNode.DeepEquals(oldSummary, combinedSummary));
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
    }

    [Fact]
    public async Task CombinedResponsePreservesEmptyLegacyAndTrackingScopesWithoutChangingOldContract()
    {
        await using var db = Database();
        var empty = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, includeSummary: true));
        Assert.Empty(empty["items"]!.AsArray());
        Assert.Equal(0, empty["summary"]!["total"]!.GetValue<int>());
        Assert.Null(empty["summary"]!["successRate"]);
        var config = new AnalysisConfig { Id = Guid.NewGuid(), CompanyId = "company", ConnectionId = Guid.NewGuid() };
        var query = new QueryHistory { Id = Guid.NewGuid(), ConfigId = config.Id, Status = "completed",
            CreatedAt = DateTime.UtcNow.AddDays(-60), CompletedAt = DateTime.UtcNow.AddDays(-60).AddSeconds(1) };
        db.AddRange(config, query); await db.SaveChangesAsync();
        var found = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, companyId: "company",
            search: query.Id.ToString(), includeSummary: true));
        Assert.Single(found["items"]!.AsArray());
        Assert.True(found["items"]![0]!["legacy"]!.GetValue<bool>());
        Assert.Equal(1, found["summary"]!["completed"]!.GetValue<int>());
        var mismatch = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, companyId: "other",
            search: query.Id.ToString(), includeSummary: true));
        Assert.Empty(mismatch["items"]!.AsArray());
        Assert.Equal(0, mismatch["summary"]!["total"]!.GetValue<int>());
        var original = Body(await DiagnosticEndpoints.List(db, Admin().Object, default));
        Assert.False(original.AsObject().ContainsKey("summary"));
        Assert.False(original.AsObject().ContainsKey("capturedAt"));
    }

    [Fact]
    public async Task CombinedResponseRejectsUnauthorizedCallBeforeDatabaseAccess()
    {
        var result = await DiagnosticEndpoints.List(null!, Admin(false).Object, default, includeSummary: true);
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
    }

    [Fact]
    public async Task MergedPagesCoverDiagnosticAndLegacySourcesWithoutGapsOrDuplicates()
    {
        await using var db = Database();
        var config = new AnalysisConfig { Id = Guid.NewGuid(), CompanyId = "company", ConnectionId = Guid.NewGuid() };
        db.Add(config);
        var created = DateTime.UtcNow.AddHours(-1);
        var expected = new HashSet<Guid>();
        for (var i = 0; i < 121; i++)
        {
            var id = Guid.NewGuid(); expected.Add(id);
            if (i % 2 == 0)
                db.Add(new QueryDiagnostic { Id = id, CreatedAt = created, CompanyId = config.CompanyId,
                    ConnectionId = config.ConnectionId, Status = "completed" });
            else
                db.Add(new QueryHistory { Id = id, ConfigId = config.Id, CreatedAt = created, Status = "completed" });
        }
        await db.SaveChangesAsync();
        var actual = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var list = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, page: page, companyId: config.CompanyId));
            Assert.Equal(121, list["total"]!.GetValue<int>());
            actual.AddRange(list["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()));
        }
        Assert.Equal(121, actual.Count);
        Assert.Equal(expected, actual.ToHashSet());
        var beyond = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, page: int.MaxValue));
        Assert.Empty(beyond["items"]!.AsArray());
        Assert.Equal(121, beyond["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task CountsIncludeAllMatchesBeyondFormerSampleAndDoNotChangeAcrossPages()
    {
        await using var db = Database();
        var connection = Guid.NewGuid();
        db.QueryDiagnostics.AddRange(Enumerable.Range(0, 5101).Select(i => new QueryDiagnostic {
            Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddMinutes(-i), CompanyId = "target",
            ConnectionId = connection, Status = "completed", Question = "Ciro" }));
        db.QueryDiagnostics.Add(new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
            CompanyId = "other", Status = "failed" });
        await db.SaveChangesAsync();
        var list = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, companyId: "target", connectionId: connection));
        var last = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, page: 103, companyId: "target", connectionId: connection));
        var summary = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default, companyId: "target", connectionId: connection));
        Assert.Equal(5101, list["total"]!.GetValue<int>());
        Assert.Equal(5101, last["total"]!.GetValue<int>());
        Assert.Equal(5101, summary["completed"]!.GetValue<int>());
        Assert.Equal(list["total"]!.GetValue<int>(), summary["total"]!.GetValue<int>());
        Assert.Equal(50, list["items"]!.AsArray().Count);
        Assert.Single(last["items"]!.AsArray());
        Assert.False(list["sampled"]!.GetValue<bool>());
        Assert.False(summary["sampled"]!.GetValue<bool>());
        Assert.Null(list["items"]![0]!["context"]);
        var combined = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, page: 103,
            companyId: "target", connectionId: connection, includeSummary: true));
        Assert.Single(combined["items"]!.AsArray());
        Assert.Equal(5101, combined["total"]!.GetValue<int>());
        Assert.Equal(5101, combined["summary"]!["completed"]!.GetValue<int>());
    }

    [Fact]
    public async Task OlderScopedRequestsRemainDiscoverableBehindThousandsOfOtherCompanies()
    {
        await using var db = Database();
        db.QueryDiagnostics.AddRange(Enumerable.Range(0, 5001).Select(i => new QueryDiagnostic {
            Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, CompanyId = "other", Status = "completed" }));
        var row = new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddDays(-2),
            CompanyId = "target", Status = "failed", ErrorCode = "empty_result", Question = "Ciro" };
        db.Add(row); await db.SaveChangesAsync();
        var list = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, companyId: "target", errorCode: "empty_result"));
        var summary = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default, companyId: "target", errorCode: "empty_result"));
        Assert.Equal(1, list["total"]!.GetValue<int>());
        Assert.Equal(1, summary["emptyResults"]!.GetValue<int>());
        Assert.Equal(row.Id, list["items"]![0]!["id"]!.GetValue<Guid>());
    }

    [Theory]
    [InlineData(7, null, null, null, null, 3)]
    [InlineData(7, "company", null, null, null, 2)]
    [InlineData(7, "company", "failed", null, null, 1)]
    [InlineData(7, "company", null, "empty_result", null, 1)]
    [InlineData(7, "company", null, null, "Ciro", 1)]
    [InlineData(1, "company", null, null, null, 1)]
    public async Task ListAndSummaryShareAllFiltersAndLegacyHistory(int days, string? company, string? status,
        string? errorCode, string? search, int expected)
    {
        await using var db = Database();
        var connection = Guid.NewGuid();
        var config = new AnalysisConfig { Id = Guid.NewGuid(), CompanyId = "company", ConnectionId = connection };
        var linked = new QueryHistory { Id = Guid.NewGuid(), ConfigId = config.Id, CreatedAt = DateTime.UtcNow,
            Status = "failed", ResultJson = "{\"audit\":{\"errorCode\":\"empty_result\"}}" };
        db.AddRange(config, linked, new QueryDiagnostic { Id = Guid.NewGuid(), QueryId = linked.Id,
            CreatedAt = linked.CreatedAt, CompanyId = "company", ConnectionId = connection, Status = "submitted", Question = "Ciro" },
            new QueryHistory { Id = Guid.NewGuid(), ConfigId = config.Id, CreatedAt = DateTime.UtcNow.AddDays(-2),
                Status = "completed", Question = "Satış" },
            new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, CompanyId = "other",
                ConnectionId = connection, Status = "completed" },
            new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, CompanyId = "company",
                ConnectionId = Guid.NewGuid(), Status = "completed" });
        await db.SaveChangesAsync();
        var list = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, days: days,
            companyId: company, connectionId: connection, status: status, errorCode: errorCode, search: search));
        var summary = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default, days: days,
            companyId: company, connectionId: connection, status: status, errorCode: errorCode, search: search));
        Assert.Equal(expected, list["total"]!.GetValue<int>());
        Assert.Equal(expected, summary["total"]!.GetValue<int>());
        Assert.Equal(expected, summary["completed"]!.GetValue<int>() + summary["failed"]!.GetValue<int>());
    }

    [Fact]
    public async Task StalePreparationAndOutOfPeriodTrackingSearchUseTheSameScopeInSummary()
    {
        await using var db = Database();
        var old = new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddDays(-60),
            CompanyId = "company", Status = "failed" };
        db.AddRange(old, new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            Status = "preparing", CompanyId = "company" });
        await db.SaveChangesAsync();
        var list = Body(await DiagnosticEndpoints.List(db, Admin().Object, default, status: "failed"));
        var summary = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default, status: "failed"));
        Assert.Equal(1, list["total"]!.GetValue<int>());
        Assert.Equal(1, summary["failed"]!.GetValue<int>());
        var lookup = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default, search: old.Id.ToString(), companyId: "company"));
        Assert.Equal(1, lookup["total"]!.GetValue<int>());
        var mismatch = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default, search: old.Id.ToString(), companyId: "other"));
        Assert.Equal(0, mismatch["total"]!.GetValue<int>());
    }

    [Fact]
    public void ScopedFiltersTranslateToPostgresSqlWithoutOpeningAConnection()
    {
        using var db = new DataAnalysisDbContext(new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=translation_only;Username=unused;Password=unused").Options);
        var query = (IQueryable)typeof(DiagnosticEndpoints).GetMethod("ScopedDiagnostics", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [db, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddMinutes(-15), "company", Guid.NewGuid(), "failed"])!;
        var sql = query.ToQueryString();
        Assert.Contains("LEFT JOIN", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains("CompanyId", sql);
        Assert.Contains("ConnectionId", sql);
        Assert.DoesNotContain("LIMIT", sql);
        var statuses = (IQueryable<string>)typeof(DiagnosticEndpoints).GetMethod("ScopedStatuses", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [db, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddMinutes(-15), "company", Guid.NewGuid(), "failed"])!;
        var aggregateSql = statuses.GroupBy(value => value).Select(g => new { Status = g.Key, Count = g.Count() }).ToQueryString();
        Assert.Contains("UNION ALL", aggregateSql);
        Assert.Contains("GROUP BY", aggregateSql);
        Assert.Contains("NOT EXISTS", aggregateSql);
        Assert.DoesNotContain("LIMIT", aggregateSql);
    }

    [Fact]
    public async Task SummaryPartitionsEveryStatusAndCountsEmptyResultsOnlyWithinFailures()
    {
        await using var db = Database();
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
        var statuses = new[] { "completed", "failed", "failed", "preparing", "submitted", "queued",
            "processing", "cancelling", "clarification", "cancelled", "future_status" };
        foreach (var status in statuses.Select((value, index) => (value, index)))
            db.QueryDiagnostics.Add(new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
                Status = status.value, ErrorCode = status.index == 1 || status.value == "completed" ? "empty_result" : null,
                DurationMs = status.value == "completed" ? 100 : status.value == "failed" ? 200
                    : status.value == "clarification" ? 300 : status.value == "cancelled" ? 400 : 99999 });
        await db.SaveChangesAsync();
        var summary = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default));
        Assert.Equal(11, summary["total"]!.GetValue<int>());
        Assert.Equal(1, summary["completed"]!.GetValue<int>());
        Assert.Equal(2, summary["failed"]!.GetValue<int>());
        Assert.Equal(1, summary["emptyResults"]!.GetValue<int>());
        Assert.Equal(5, summary["pending"]!.GetValue<int>());
        Assert.Equal(1, summary["clarification"]!.GetValue<int>());
        Assert.Equal(1, summary["cancelled"]!.GetValue<int>());
        Assert.Equal(1, summary["other"]!.GetValue<int>());
        Assert.Equal(summary["total"]!.GetValue<int>(), new[] { "completed", "failed", "pending", "clarification", "cancelled", "other" }
            .Sum(key => summary[key]!.GetValue<int>()));
        Assert.Equal(1d / 3, summary["successRate"]!.GetValue<double>(), 10);
        Assert.Equal(5, summary["terminalCount"]!.GetValue<int>());
        Assert.Equal(400, summary["p95Ms"]!.GetValue<int>());
    }

    [Fact]
    public async Task EmptyOrPendingOnlyPopulationHasNoSuccessRateOrTerminalLatency()
    {
        await using var db = Database();
        var empty = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default));
        Assert.Null(empty["successRate"]);
        Assert.Null(empty["p95Ms"]);
        db.QueryDiagnostics.Add(new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, Status = "processing" });
        await db.SaveChangesAsync();
        var pending = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default));
        Assert.Null(pending["successRate"]);
        Assert.Null(pending["p95Ms"]);
        Assert.Equal(0, pending["terminalCount"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("{\"errorCode\":\"timeout\",\"audit\":{\"errorCode\":\"empty_result\"}}", "timeout")]
    [InlineData("{\"audit\":{\"errorCode\":\"empty_result\"}}", "empty_result")]
    [InlineData("{\"errorCode\":\" \",\"audit\":{\"errorCode\":\"empty_result\"}}", "empty_result")]
    [InlineData("{}", "stored_code")]
    public void StructuredErrorCodesUseTheSamePrecedenceInDiagnosticViews(string json, string expected)
    {
        var result = QueryDiagnostics.Parse(json);
        Assert.Equal(expected, QueryDiagnostics.ResolveErrorCode(result, "stored_code", "timeout"));
        var view = DiagnosticEndpoints.ToView(new QueryDiagnostic { ErrorCode = "stored_code" },
            new QueryHistory { Status = "failed", ResultJson = json });
        Assert.Equal(expected, view.ErrorCode);
        Assert.Equal("empty_result", QueryDiagnostics.ResolveErrorCode(null, null, "Sorguya uyan kayıt bulunamadı."));
        Assert.Null(DiagnosticEndpoints.ToView(new QueryDiagnostic { Status = "completed", ErrorCode = "empty_result" }).ErrorCode);
    }

    [Fact]
    public async Task LinkedHistoryIsCountedOnceAndOverridesSubmittedStatus()
    {
        await using var db = Database();
        var config = new AnalysisConfig { Id = Guid.NewGuid(), CompanyId = "company", ConnectionId = Guid.NewGuid() };
        var query = new QueryHistory { Id = Guid.NewGuid(), ConfigId = config.Id, CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow, Status = "failed", ResultJson = "{\"audit\":{\"errorCode\":\"empty_result\"}}" };
        db.AddRange(config, query, new QueryDiagnostic { Id = Guid.NewGuid(), QueryId = query.Id,
            CreatedAt = DateTime.UtcNow, Status = "submitted" });
        await db.SaveChangesAsync();
        var summary = Body(await DiagnosticEndpoints.Summary(db, Admin().Object, default));
        Assert.Equal(1, summary["total"]!.GetValue<int>());
        Assert.Equal(1, summary["failed"]!.GetValue<int>());
        Assert.Equal(1, summary["emptyResults"]!.GetValue<int>());
        Assert.Equal(0, summary["pending"]!.GetValue<int>());
        Assert.Equal(0d, summary["successRate"]!.GetValue<double>());
    }

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

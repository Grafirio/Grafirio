using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Features.Admin;
using Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class DiagnosticVisibilityTests
{
    private static DataAnalysisDbContext Database() => new(new DbContextOptionsBuilder<DataAnalysisDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static IIdentityService Admin()
    {
        var identity = new Mock<IIdentityService>(MockBehavior.Strict);
        identity.Setup(x => x.HasBusinessRole(SemanticSignalEndpoints.PlatformAdminRole)).Returns(true);
        return identity.Object;
    }
    private static JsonNode Json(object value) => JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static JsonNode Body(IResult result) => Json(((IValueHttpResult)result).Value!);
    private static DiagnosticEndpoints.View View(string status, int duration, string company = "a", Guid? connection = null,
        bool complete = true, string review = "new", short? feedback = null) => DiagnosticEndpoints.ToView(new QueryDiagnostic {
            Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddHours(-1), Status = status, DurationMs = duration,
            CompletedAt = complete ? DateTime.UtcNow : null, CompanyId = company, ConnectionId = connection ?? Guid.Empty,
            ReviewStatus = review, Question = "Ciro", ErrorCode = status == "failed" ? "empty_result" : null
        }) with { FeedbackRating = feedback };

    [Fact]
    public void LatencyUsesTheSameKnownTerminalPopulationForMeanAndNearestRankP95()
    {
        var rows = Enumerable.Range(1, 20).Select(i => View("completed", i * 100)).ToList();
        rows.Add(View("processing", 99999, complete: false));
        rows.Add(View("failed", 0, complete: false));
        var latency = DiagnosticVisibility.MeasureLatency(rows);
        Assert.Equal(20, latency.SampleCount);
        Assert.Equal(1, latency.MissingCount);
        Assert.Equal(1050d, latency.AverageMs);
        Assert.Equal(1900, latency.P95Ms);
        var empty = DiagnosticVisibility.MeasureLatency([View("failed", 0, complete: false)]);
        Assert.Null(empty.AverageMs); Assert.Null(empty.P95Ms);
    }

    [Fact]
    public void BreakdownsPreserveTotalsAndConnectionIdentityIncludesCompany()
    {
        var connection = Guid.NewGuid();
        var rows = new List<DiagnosticEndpoints.View> { View("completed", 100, "a", connection),
            View("failed", 300, "a", connection), View("clarification", 200, "b", connection),
            View("future_status", 0, "b", connection) };
        var visibility = Json(DiagnosticVisibility.Build(rows, new DiagnosticSettings()));
        Assert.Equal(4, visibility["statusDistribution"]!.AsArray().Sum(x => x!["count"]!.GetValue<int>()));
        Assert.Equal(2, visibility["byConnection"]!.AsArray().Count);
        Assert.Equal(4, visibility["byCompany"]!.AsArray().Sum(x => x!["metrics"]!["total"]!.GetValue<int>()));
        Assert.Equal(1, visibility["byCompany"]![0]!["metrics"]!["failed"]!.GetValue<int>());
        Assert.Equal(1, visibility["byCompany"]![0]!["metrics"]!["emptyResults"]!.GetValue<int>());
        Assert.Equal("other", visibility["statusDistribution"]!.AsArray().Single(x => x!["status"]!.GetValue<string>() == "future_status")!["group"]!.GetValue<string>());
    }

    [Fact]
    public void ReviewCandidatesAreUniqueReasonedAndExcludeResolvedRecords()
    {
        var issue = View("failed", 2000, feedback: -1);
        var rows = new List<DiagnosticEndpoints.View> { issue, View("failed", 2000, review: "fixed"),
            View("failed", 2000, review: "verified"), View("completed", 100), View("clarification", 100) };
        var view = Json(DiagnosticVisibility.Build(rows, new DiagnosticSettings { SlowQuerySeconds = 1 }));
        Assert.Equal(2, view["reviewCandidateCount"]!.GetValue<int>());
        var first = view["reviewCandidates"]![0]!;
        Assert.Equal(issue.Id, first["id"]!.GetValue<Guid>());
        Assert.Equal(new[] { "failed", "negative_feedback", "slow" }, first["reasons"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public void ComparisonShowsPercentagePointChangeAndNoInventedValuesForEmptyPeriods()
    {
        var now = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var current = new List<DiagnosticEndpoints.View> { View("completed", 100), View("completed", 200) };
        var previous = new List<DiagnosticEndpoints.View> { View("completed", 300), View("failed", 500) };
        var comparison = Json(DiagnosticVisibility.Compare(current, previous, now, 7, 90, false));
        Assert.Equal(50d, comparison["successRateChangePoints"]!.GetValue<double>());
        Assert.Equal(-250d, comparison["averageChangeMs"]!.GetValue<double>());
        Assert.Equal(comparison["currentFrom"]!.GetValue<DateTime>(), comparison["previousUntil"]!.GetValue<DateTime>());
        Assert.Equal(TimeSpan.FromDays(7), comparison["currentUntil"]!.GetValue<DateTime>() - comparison["currentFrom"]!.GetValue<DateTime>());
        var noPrevious = Json(DiagnosticVisibility.Compare(current, [], now, 90, 90, false));
        Assert.Null(noPrevious["successRateChangePoints"]);
        Assert.Null(noPrevious["averageChangeMs"]);
        Assert.True(noPrevious["retentionMayLimitHistory"]!.GetValue<bool>());
        var lookup = Json(DiagnosticVisibility.Compare(current, [], now, 7, 90, true));
        Assert.False(lookup["available"]!.GetValue<bool>());
        Assert.Null(lookup["previous"]);
        Assert.Null(lookup["successRateChangePoints"]);
    }

    [Fact]
    public async Task PeriodBoundariesAreExclusiveAtEndForBothDiagnosticAndLegacySources()
    {
        await using var db = Database();
        var now = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var start = now.AddDays(-7); var previousStart = start.AddDays(-7);
        var config = new AnalysisConfig { Id = Guid.NewGuid(), CompanyId = "a" };
        db.Add(config);
        foreach (var at in new[] { previousStart, start, now })
        {
            db.Add(new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = at, Status = "completed", CompanyId = "a" });
            db.Add(new QueryHistory { Id = Guid.NewGuid(), CreatedAt = at, Status = "completed", ConfigId = config.Id });
        }
        await db.SaveChangesAsync();
        async Task<List<DiagnosticEndpoints.View>> Read(DateTime from, DateTime until) => await (Task<List<DiagnosticEndpoints.View>>)
            typeof(DiagnosticEndpoints).GetMethod("LoadFiltered", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [db, Admin(), 7, null, null, null, "a", null, CancellationToken.None, now, from, until])!;
        var current = await Read(start, now); var previous = await Read(previousStart, start);
        Assert.Equal(2, current.Count); Assert.Equal(2, previous.Count);
        Assert.Empty(current.Select(r => r.Id).Intersect(previous.Select(r => r.Id)));
        Assert.All(current, r => Assert.Equal(start, r.CreatedAt));
    }

    [Fact]
    public async Task EndpointComparesFullPreviousWindowAndKeepsItsScope()
    {
        await using var db = Database();
        db.AddRange(new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddDays(-1), CompanyId = "a", Status = "completed" },
            new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddDays(-400), CompanyId = "a", Status = "failed" },
            new QueryDiagnostic { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddDays(-400), CompanyId = "b", Status = "completed" });
        await db.SaveChangesAsync();
        var result = Body(await DiagnosticEndpoints.List(db, Admin(), default, days: 365, companyId: "a", includeSummary: true));
        Assert.Equal(1, result["total"]!.GetValue<int>());
        Assert.Equal(1, result["summary"]!["comparison"]!["previous"]!["total"]!.GetValue<int>());
        Assert.Equal(1, result["summary"]!["comparison"]!["previous"]!["failed"]!.GetValue<int>());
        var filtered = Body(await DiagnosticEndpoints.Summary(db, Admin(), default, days: 365, companyId: "a", status: "completed"));
        Assert.Equal(0, filtered["comparison"]!["previous"]!["total"]!.GetValue<int>());
    }
}

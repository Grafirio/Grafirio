using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Tests;

public sealed class ConnectionRouteSaveFailureTests
{
    [Fact]
    public async Task SourceChangeSavesSqlThenForgetsOldCredentialsAndClearsScopeBeforePublish()
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        var analysis = new AnalysisConfig
        {
            Id = Guid.NewGuid(), ConnectionId = harness.Connection.Id, CompanyId = harness.Connection.CompanyId,
            IsActive = true, Status = "ready"
        };
        harness.Db.Add(analysis);
        await harness.Db.SaveChangesAsync();
        harness.Events.Clear();
        var result = await harness.UpdateAsync();
        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(["reserve", "sql", "forget", "clear-tables", "publish"], harness.Events);
        Assert.False(analysis.IsActive);
        Assert.True(harness.Route.Matches(await harness.Db.SavedConnections.AsNoTracking().SingleAsync()));
    }

    [Fact]
    public async Task SqlFailureRestoresPreviousRouteOnlyWhenDatabaseStillMatches()
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        var previous = harness.Route;
        harness.FailSql = true;
        var result = await harness.UpdateAsync();
        Assert.Equal(StatusCodes.Status500InternalServerError, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(previous, harness.Route);
        Assert.True(harness.Route.Matches(await harness.Db.SavedConnections.AsNoTracking().SingleAsync()));
        Assert.DoesNotContain("forget", harness.Events);
    }

    [Theory]
    [InlineData("publication")]
    [InlineData("cleanup")]
    [InlineData("ambiguous-commit")]
    public async Task FailureAfterSqlLeavesRouteBlockedWithoutRestoringOldBinding(string failure)
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        harness.FailPublish = failure == "publication";
        harness.FailCleanup = failure == "cleanup";
        harness.FailAfterSql = failure == "ambiguous-commit";
        await harness.UpdateAsync();
        var persisted = await harness.Db.SavedConnections.AsNoTracking().SingleAsync();
        Assert.Equal("new-host", persisted.Host);
        Assert.True(harness.Route.Pending);
        Assert.True(harness.Route.Failed);
        Assert.Throws<DataSourceException>(() => harness.Route.ValidateBinding(persisted));
        Assert.DoesNotContain("publish", harness.Events);
        var result = await harness.GetAsync();
        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.False(System.Text.Json.JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value)
            .GetProperty("routeAvailable").GetBoolean());
    }

    [Fact]
    public async Task NewConnectionPublicationFailureCannotLeaveImplicitDirectRoute()
    {
        await using var harness = new ConnectionRouteReviewHarness { FailPublish = true };
        await harness.CreateAsync();
        var saved = await harness.Db.SavedConnections.AsNoTracking().SingleAsync();
        Assert.True(harness.Route.Pending);
        Assert.Throws<DataSourceException>(() => harness.Route.ValidateBinding(saved));
        Assert.Equal(["reserve", "sql", "fail"], harness.Events);
    }

    [Fact]
    public async Task NewConnectionSqlFailureRetainsPendingReservation()
    {
        await using var harness = new ConnectionRouteReviewHarness { FailSql = true };
        await harness.CreateAsync();
        Assert.Empty(await harness.Db.SavedConnections.AsNoTracking().ToListAsync());
        Assert.True(harness.Route.Pending);
    }

    [Fact]
    public async Task FailedReservationDoesNotMutateOrSaveCredentials()
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        var previous = harness.Route;
        harness.FailReserve = true;
        await harness.UpdateAsync();
        Assert.Equal("old-host", harness.Connection.Host);
        Assert.Equal(previous, harness.Route);
        Assert.Empty(harness.Events);
    }
}
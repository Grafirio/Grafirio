using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class ConnectionRouteReviewTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TargetChangeWithoutPasswordIsRejectedBeforeAnyWrite(bool sameNameCreate, bool bridgeChange)
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        var original = harness.Route;
        var bridgeId = bridgeChange ? Guid.NewGuid() : (Guid?)null;
        var mode = bridgeChange ? ConnectionRoute.Bridge : ConnectionRoute.Direct;
        var host = bridgeChange ? harness.Connection.Host : "new-host";
        var result = sameNameCreate
            ? await harness.SaveAsync(new SaveConnectionRequest("ignored", "ignored", harness.Connection.Name, host,
                harness.Connection.Port, harness.Connection.Database, harness.Connection.Username, "",
                harness.Connection.TrustServerCertificate, mode, bridgeId))
            : await harness.UpdateAsync(new UpdateConnectionRequest(null, host, null, null, null, null, null, mode, bridgeId));

        AssertStatus(StatusCodes.Status400BadRequest, result);
        Assert.Equal(original, harness.Route);
        Assert.Equal("old-host", harness.Connection.Host);
        Assert.Equal("encrypted-old", harness.Connection.EncryptedPassword);
        Assert.Empty(harness.Events);
        Assert.Equal("old-host", (await harness.Db.SavedConnections.AsNoTracking().SingleAsync()).Host);
    }

    [Fact]
    public async Task ExplicitSamePlaintextPasswordAllowsTargetChange()
    {
        await using var harness = new ConnectionRouteReviewHarness();
        harness.Connection.EncryptedPassword = EncryptionHelper.Encrypt("same-password");
        await harness.SeedAsync();
        var ciphertext = harness.Connection.EncryptedPassword;
        var result = await harness.UpdateAsync(Change("same-password"));
        AssertStatus(StatusCodes.Status200OK, result);
        Assert.NotEqual(ciphertext, harness.Connection.EncryptedPassword);
        Assert.Equal("same-password", EncryptionHelper.Decrypt(harness.Connection.EncryptedPassword));
    }

    [Fact]
    public async Task NameOnlyUpdateRetainsPasswordAndTableScope()
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        AssertStatus(StatusCodes.Status200OK, await harness.UpdateAsync(
            new UpdateConnectionRequest("renamed", null, null, null, null, null, null)));
        Assert.Equal("encrypted-old", harness.Connection.EncryptedPassword);
        Assert.DoesNotContain("clear-tables", harness.Events);
    }

    [Theory]
    [InlineData("cleanup")]
    [InlineData("tables")]
    [InlineData("publication")]
    [InlineData("commit")]
    public async Task FailedSaveStaysBlockedUntilExplicitFreshRetryClearsScope(string failure)
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        harness.FailCleanup = failure == "cleanup";
        harness.FailTables = failure == "tables";
        harness.FailPublish = failure == "publication";
        harness.FailAfterSql = failure == "commit";
        var failed = await harness.UpdateAsync(Change());
        Assert.NotEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)failed).StatusCode);
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(((IValueHttpResult)failed).Value));
        Assert.True(harness.Route.Pending);
        Assert.True(harness.Route.Failed);
        Assert.Throws<DataSourceException>(() => harness.Route.ValidateBinding(harness.Connection));

        harness.FailCleanup = harness.FailTables = harness.FailPublish = harness.FailAfterSql = false;
        harness.Db.ChangeTracker.Clear();
        harness.Events.Clear();
        AssertStatus(StatusCodes.Status400BadRequest, await harness.UpdateAsync(
            new UpdateConnectionRequest(null, null, null, null, null, "fresh-password", null)));
        AssertStatus(StatusCodes.Status400BadRequest, await harness.UpdateAsync(
            new UpdateConnectionRequest(null, null, null, null, null, null, null, ConnectionRoute.Direct)));
        Assert.Empty(harness.Events);

        // Even a retry with the already committed source must discard the old selected-table scope.
        AssertStatus(StatusCodes.Status200OK, await harness.UpdateAsync(Change()));
        Assert.Equal(["reserve", "sql", "forget", "clear-tables", "publish"], harness.Events);
        Assert.False(harness.Route.Pending);
        Assert.False(harness.Route.Failed);
        Assert.True(harness.Route.Matches(await harness.Db.SavedConnections.AsNoTracking().SingleAsync()));
    }

    [Fact]
    public async Task FailedRetryDeactivatesAnalysesEvenWhenSourceFieldsAreUnchanged()
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        harness.Route = harness.Route with { Pending = true, Failed = true, Fingerprint = "older-snapshot" };
        var analysis = new AnalysisConfig { Id = Guid.NewGuid(), ConnectionId = harness.Connection.Id,
            CompanyId = harness.Connection.CompanyId, IsActive = true, Status = "ready" };
        harness.Db.Add(analysis);
        await harness.Db.SaveChangesAsync();
        AssertStatus(StatusCodes.Status200OK, await harness.UpdateAsync(
            new UpdateConnectionRequest(null, null, null, null, null, "fresh-password", null, ConnectionRoute.Direct)));
        Assert.False(analysis.IsActive);
        Assert.Contains("clear-tables", harness.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivePendingAndUnmarkedMismatchCannotBeReplaced(bool pending)
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        harness.Route = harness.Route with { Pending = pending, Fingerprint = pending ? harness.Route.Fingerprint : "mismatch" };
        var previous = harness.Route;
        AssertStatus(StatusCodes.Status400BadRequest, await harness.UpdateAsync(Change()));
        Assert.Equal(previous, harness.Route);
        Assert.Empty(harness.Events);
    }

    [Fact]
    public async Task FailedReservationRequiresFreshDatabaseSnapshot()
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        harness.Route = harness.Route with { Pending = true, Failed = true };
        // Simulate an edit loaded before a different SQL snapshot was committed.
        harness.Connection.Host = "stale-host";
        harness.Events.Clear();
        AssertStatus(StatusCodes.Status400BadRequest, await harness.UpdateAsync(Change()));
        Assert.DoesNotContain("sql", harness.Events);
        Assert.True(harness.Route.Failed);
        Assert.Equal("old-host", (await harness.Db.SavedConnections.AsNoTracking().SingleAsync()).Host);
    }

    [Fact]
    public async Task MissingRouteRequiresExplicitTargetAndPasswordRatherThanImplicitDirect()
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        harness.Route = new ConnectionRoute();
        AssertStatus(StatusCodes.Status400BadRequest, await harness.UpdateAsync(
            new UpdateConnectionRequest("rename", null, null, null, null, null, null)));
        AssertStatus(StatusCodes.Status400BadRequest, await harness.UpdateAsync(
            new UpdateConnectionRequest(null, null, null, null, null, null, null, ConnectionRoute.Direct)));
        Assert.Empty(harness.Events);
        AssertStatus(StatusCodes.Status200OK, await harness.UpdateAsync(Change() with
            { ConnectionMode = ConnectionRoute.Bridge, BridgeId = Guid.NewGuid() }));
        Assert.Equal(ConnectionRoute.Bridge, harness.Route.ConnectionMode);
        Assert.Contains("clear-tables", harness.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableRowCanBeReadForEditingWithoutBreakingHealthyList(bool pending)
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        var healthyRoute = harness.Route;
        var legacy = new SavedConnection { Id = Guid.NewGuid(), CompanyId = harness.Connection.CompanyId,
            Name = "legacy", Host = "legacy-host", CreatedAt = DateTime.UtcNow };
        harness.Db.Add(legacy);
        await harness.Db.SaveChangesAsync();
        var unavailable = pending ? healthyRoute with { Pending = true, Failed = true } : new ConnectionRoute();
        harness.Store.Setup(store => store.GetConnectionRouteAsync(legacy.Id, legacy.CompanyId,
            Moq.It.IsAny<CancellationToken>())).ReturnsAsync(unavailable);
        var list = await harness.GetAsync(list: true);
        AssertStatus(StatusCodes.Status200OK, list);
        var rows = JsonSerializer.SerializeToElement(((IValueHttpResult)list).Value).GetProperty("connections")
            .EnumerateArray().ToList();
        Assert.True(rows.Single(row => row.GetProperty("id").GetGuid() == harness.Connection.Id).GetProperty("routeAvailable").GetBoolean());
        Assert.False(rows.Single(row => row.GetProperty("id").GetGuid() == legacy.Id).GetProperty("routeAvailable").GetBoolean());

        harness.Route = unavailable;
        var single = await harness.GetAsync();
        AssertStatus(StatusCodes.Status200OK, single);
        var body = JsonSerializer.SerializeToElement(((IValueHttpResult)single).Value);
        Assert.False(body.GetProperty("routeAvailable").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("routeError").GetString()));
        Assert.Equal("old-host", body.GetProperty("host").GetString());
        Assert.False(body.TryGetProperty("encryptedPassword", out _));
        Assert.Throws<DataSourceException>(() => unavailable.ValidateBinding(harness.Connection));
    }

    private static UpdateConnectionRequest Change(string password = "fresh-password") =>
        new(null, "new-host", null, null, null, password, null, ConnectionRoute.Direct);

    private static void AssertStatus(int expected, IResult result) =>
        Assert.Equal(expected, ((IStatusCodeHttpResult)result).StatusCode);
}
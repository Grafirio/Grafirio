using System.Reflection;
using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class ConnectionPreviewTests
{
    private static readonly Guid CompanyId = Guid.Parse(BridgeRoutingHarness.Company);

    [Fact]
    public async Task UnsavedPreviewPassesExactRouteAndAuthenticatedCompanyWithoutSaving()
    {
        const bool databases = false;
        var harness = new BridgeRoutingHarness();
        await using var db = CreateDatabase();
        var sources = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        var session = Session(databases);
        DataSourceTarget? target = null;
        sources.Setup(value => value.OpenAsync(It.IsAny<DataSourceTarget>(), It.IsAny<CancellationToken>()))
            .Callback<DataSourceTarget, CancellationToken>((value, _) => target = value).ReturnsAsync(session.Object);
        var request = new ConnectionPreviewRequest("customer-pc", 1433, "customer", "reader", "test-only-password",
            ConnectionMode: ConnectionRoute.Bridge, BridgeId: BridgeRoutingHarness.SelectedBridge);

        var result = await InvokeAsync(request, Identity(), db, sources.Object, harness, databases);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(BridgeRoutingHarness.Company, target!.CompanyId);
        Assert.Equal(harness.Route.ConnectionMode, target.Route.ConnectionMode);
        Assert.Equal(harness.Route.BridgeId, target.Route.BridgeId);
        Assert.Null(target.Route.Fingerprint);
        Assert.Equal("customer", target.Database);
        Assert.Empty(await db.SavedConnections.ToListAsync());
        Assert.DoesNotContain("test-only-password", JsonSerializer.Serialize(((IValueHttpResult)result).Value));
    }

    [Fact]
    public async Task DatabaseDiscoveryReportsExistingPolicyRestrictionWithoutBypassingGuard()
    {
        var harness = new BridgeRoutingHarness();
        await using var db = CreateDatabase();
        var sources = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        var request = new ConnectionPreviewRequest("customer-pc", 1433, "customer", "reader", "test-only-password",
            ConnectionMode: ConnectionRoute.Bridge, BridgeId: BridgeRoutingHarness.SelectedBridge);
        var result = await InvokeAsync(request, Identity(), db, sources.Object, harness, true);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Contains("sql_policy_blocked", JsonSerializer.Serialize(((IValueHttpResult)result).Value));
        Assert.Empty(await db.SavedConnections.ToListAsync());
        sources.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("test-only-stored-password")]
    public async Task SameTargetPreviewKeepsBridgeAndPasswordWithoutChangingSavedValues(string? password)
    {
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", "test-only-connection-encryption-key");
        var harness = new BridgeRoutingHarness();
        await using var db = CreateDatabase();
        var saved = harness.Saved;
        saved.EncryptedPassword = EncryptionHelper.Encrypt("test-only-stored-password");
        harness.BindRoute();
        db.Add(saved);
        await db.SaveChangesAsync();
        var sources = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        sources.Setup(value => value.OpenAsync(It.Is<DataSourceTarget>(target =>
                target.Route == harness.Route && target.Password == "test-only-stored-password" &&
                target.Host == saved.Host), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Session(false).Object);

        var result = await InvokeAsync(new ConnectionPreviewRequest(saved.Host, saved.Port, saved.Database, saved.Username, password,
            TrustServerCertificate: saved.TrustServerCertificate,
            ConnectionId: saved.Id), Identity(), db, sources.Object, harness, false);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal("customer-pc", saved.Host);
        Assert.Equal("test-only-stored-password", EncryptionHelper.Decrypt(saved.EncryptedPassword));
        Assert.Equal(EntityState.Unchanged, db.Entry(saved).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignSavedConnectionCannotSupplyCredentials(bool databases)
    {
        var harness = new BridgeRoutingHarness();
        await using var db = CreateDatabase();
        var saved = harness.Saved;
        saved.CompanyId = "another-company";
        db.Add(saved);
        await db.SaveChangesAsync();
        var sources = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        var result = await InvokeAsync(new ConnectionPreviewRequest("host", 1433, "db", "reader", null,
            ConnectionId: saved.Id), Identity(), db, sources.Object, harness, databases);
        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
        sources.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(ConnectionPreviewRequest.BridgeId))]
    [InlineData(nameof(ConnectionPreviewRequest.Host))]
    [InlineData(nameof(ConnectionPreviewRequest.Username))]
    [InlineData(nameof(ConnectionPreviewRequest.Port))]
    [InlineData(nameof(ConnectionPreviewRequest.Database))]
    [InlineData(nameof(ConnectionPreviewRequest.TrustServerCertificate))]
    [InlineData(nameof(ConnectionPreviewRequest.ConnectionMode))]
    public async Task ChangedTargetCannotReuseStoredPassword(string changedField)
    {
        var harness = new BridgeRoutingHarness();
        await using var db = CreateDatabase();
        var saved = harness.Saved;
        db.Add(saved);
        await db.SaveChangesAsync();
        var sources = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        var request = new ConnectionPreviewRequest(saved.Host,
            saved.Port, saved.Database, saved.Username, null, saved.TrustServerCertificate,
            ConnectionRoute.Bridge, BridgeRoutingHarness.SelectedBridge, saved.Id);
        request = changedField switch
        {
            nameof(ConnectionPreviewRequest.BridgeId) => request with { BridgeId = BridgeRoutingHarness.OtherBridge },
            nameof(ConnectionPreviewRequest.Host) => request with { Host = "another-host" },
            nameof(ConnectionPreviewRequest.Username) => request with { Username = "another-reader" },
            nameof(ConnectionPreviewRequest.Port) => request with { Port = saved.Port + 1 },
            nameof(ConnectionPreviewRequest.Database) => request with { Database = "another-database" },
            nameof(ConnectionPreviewRequest.TrustServerCertificate) => request with { TrustServerCertificate = !saved.TrustServerCertificate },
            nameof(ConnectionPreviewRequest.ConnectionMode) => request with { ConnectionMode = ConnectionRoute.Direct, BridgeId = null },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField))
        };
        var result = await InvokeAsync(request, Identity(), db, sources.Object, harness, false);
        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Contains("Bağlantı hedefi değişti", ((IValueHttpResult)result).Value!.ToString());
        sources.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvalidSavedBindingCannotSupplyCredentials(bool pending, bool databases)
    {
        var harness = new BridgeRoutingHarness();
        await using var db = CreateDatabase();
        var saved = harness.Saved;
        harness.Route = pending
            ? harness.Route with { Pending = true }
            : harness.Route with { Fingerprint = "stale-fingerprint" };
        db.Add(saved);
        await db.SaveChangesAsync();
        var sources = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        var request = new ConnectionPreviewRequest(saved.Host, saved.Port, saved.Database, saved.Username, null,
            saved.TrustServerCertificate, ConnectionId: saved.Id);

        var result = await InvokeAsync(request, Identity(), db, sources.Object, harness, databases);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Contains(pending ? "Bağlantı güncelleniyor" : "kayıtla eşleşmiyor",
            ((IValueHttpResult)result).Value!.ToString());
        Assert.Equal(EntityState.Unchanged, db.Entry(saved).State);
        sources.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("test-only-stored-password")]
    [InlineData("test-only-edited-password")]
    public async Task ExplicitPasswordAllowsEditedTargetWithoutChangingSavedValues(string password)
    {
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", "test-only-connection-encryption-key");
        var harness = new BridgeRoutingHarness();
        await using var db = CreateDatabase();
        var saved = harness.Saved;
        saved.EncryptedPassword = EncryptionHelper.Encrypt("test-only-stored-password");
        harness.BindRoute();
        var savedRoute = harness.Route;
        var savedPassword = saved.EncryptedPassword;
        db.Add(saved);
        await db.SaveChangesAsync();
        var request = new ConnectionPreviewRequest("another-host", saved.Port + 1, "another-database", "another-reader",
            password, !saved.TrustServerCertificate, ConnectionRoute.Direct, ConnectionId: saved.Id);
        var sources = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        sources.Setup(value => value.OpenAsync(It.Is<DataSourceTarget>(target =>
                target.Host == request.Host && target.Port == request.Port && target.Database == request.Database &&
                target.Username == request.Username && target.Password == password &&
                target.TrustServerCertificate == request.TrustServerCertificate &&
                target.Route == request.Route && target.CompanyId == BridgeRoutingHarness.Company),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Session(false).Object);

        var result = await InvokeAsync(request, Identity(), db, sources.Object, harness, false);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(savedPassword, saved.EncryptedPassword);
        Assert.Equal(savedRoute, harness.Route);
        Assert.True(savedRoute.Matches(saved));
        Assert.Equal(EntityState.Unchanged, db.Entry(saved).State);
        Assert.DoesNotContain(password, JsonSerializer.Serialize(((IValueHttpResult)result).Value));
    }

    [Fact]
    public async Task OfflinePreviewReportsFailureWithoutWriting()
    {
        var harness = new BridgeRoutingHarness();
        harness.Registry.Detach(BridgeRoutingHarness.SelectedBridge, "selected-client");
        await using var db = CreateDatabase();
        var request = new ConnectionPreviewRequest("customer-pc", 1433, "customer", "reader", "test-password",
            ConnectionMode: ConnectionRoute.Bridge, BridgeId: BridgeRoutingHarness.SelectedBridge);
        var result = await InvokeAsync(request, Identity(), db, harness.Factory, harness, false);
        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Empty(await db.SavedConnections.ToListAsync());
    }

    private static Mock<IDataSourceSession> Session(bool databases)
    {
        var session = new Mock<IDataSourceSession>(MockBehavior.Strict);
        session.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        if (databases)
        {
            session.Setup(value => value.QueryRowsAsync(It.Is<string>(sql => sql.Contains("sys.databases")),
                    null, DataSourceTarget.ProbeConnectTimeoutSeconds, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new QueryRow(new Dictionary<string, object?> { ["name"] = "customer" })]);
        }
        else
        {
            session.Setup(value => value.ScalarAsync<int>("SELECT 1", null,
                DataSourceTarget.ProbeConnectTimeoutSeconds, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }
        return session;
    }

    private static IIdentityService Identity()
    {
        var identity = new Mock<IIdentityService>();
        identity.SetupGet(value => value.CurrentCompanyId).Returns(CompanyId);
        return identity.Object;
    }

    private static DataAnalysisDbContext CreateDatabase() => new(
        new DbContextOptionsBuilder<DataAnalysisDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Task<IResult> InvokeAsync(ConnectionPreviewRequest request, IIdentityService identity,
        DataAnalysisDbContext db, IDataSourceFactory sources, BridgeRoutingHarness harness, bool databases) =>
        (Task<IResult>)typeof(ConnectionPreviewEndpoints).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [request, identity, db, sources, harness.Store.Object, databases, CancellationToken.None])!;
}
using System.Reflection;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

internal sealed class ConnectionRouteReviewHarness : IAsyncDisposable
{
    internal DataAnalysisDbContext Db { get; }
    internal SavedConnection Connection { get; } = new()
    {
        Id = Guid.NewGuid(), CompanyId = BridgeRoutingHarness.Company, Name = "customer",
        Host = "old-host", Port = 1433, Database = "customer", Username = "reader",
        EncryptedPassword = "encrypted-old", CreatedAt = DateTime.UtcNow
    };
    internal ConnectionRoute Route { get; set; } = new();
    internal Mock<BridgeStore> Store { get; } = new(Mock.Of<IMongoDatabase>(), NullLogger<BridgeStore>.Instance);
    internal Mock<BridgeConnectionSync> Sync { get; }
    internal List<string> Events { get; } = [];
    internal bool FailCleanup { get; set; }
    internal bool FailTables { get; set; }
    internal bool FailPublish { get; set; }
    internal bool FailAfterSql { get; set; }
    internal bool FailSql { get; set; }
    internal bool FailReserve { get; set; }
    private readonly IIdentityService _identity;

    internal ConnectionRouteReviewHarness()
    {
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", "test-only-connection-encryption-key");
        Db = new DataAnalysisDbContext(new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new SaveObserver(this)).Options);
        var identity = new Mock<IIdentityService>();
        identity.SetupGet(value => value.CurrentCompanyId).Returns(Guid.Parse(BridgeRoutingHarness.Company));
        identity.SetupGet(value => value.UserId).Returns(Guid.NewGuid());
        _identity = identity.Object;
        Store.Setup(value => value.GetConnectionRouteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Route);
        Store.Setup(value => value.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new RegisteredBridge(id, Connection.CompanyId,
                "desktop", "machine", "1", DateTime.UtcNow, null));
        Store.Setup(value => value.ReserveConnectionRouteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<ConnectionRoute>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, ConnectionRoute, string, CancellationToken>((_, _, previous, revision, _) =>
            {
                if (FailReserve || (Route.Pending && !Route.Failed) || Route != previous) throw new DataSourceException("Reservation failed");
                Events.Add("reserve");
                Route = previous with { Pending = true, Failed = false, Revision = revision };
            }).Returns(Task.CompletedTask);
        Store.Setup(value => value.CompleteConnectionRouteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<ConnectionRoute>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, ConnectionRoute, CancellationToken>((_, _, revision, route, _) =>
            {
                if (FailPublish || !Route.Pending || Route.Failed || Route.Revision != revision)
                    throw new DataSourceException("Publication failed");
                Events.Add("publish");
                Route = route;
            }).Returns(Task.CompletedTask);
        Store.Setup(value => value.FailConnectionRouteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, CancellationToken>((_, _, revision, _) =>
            {
                Assert.Equal(Route.Revision, revision);
                Assert.True(Route.Pending);
                Events.Add("fail");
                Route = Route with { Failed = true };
            }).Returns(Task.CompletedTask);
        Sync = new Mock<BridgeConnectionSync>(Mock.Of<IHubContext<BridgeHub>>(), Store.Object,
            null!, Mock.Of<IServiceScopeFactory>(), NullLogger<BridgeConnectionSync>.Instance);
        Sync.Setup(value => value.ForgetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<BridgeRegistry>(),
                It.IsAny<CancellationToken>())).Callback(() =>
            {
                Events.Add("forget");
                Assert.True(Route.Pending);
                if (FailCleanup) throw new InvalidOperationException("private-cleanup-detail");
            }).Returns(Task.CompletedTask);
        Sync.Setup(value => value.ClearSelectedTablesAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.True(Route.Pending);
                Events.Add("clear-tables");
                if (FailTables) throw new InvalidOperationException("private-scope-detail");
            }).Returns(Task.CompletedTask);
    }

    internal async Task SeedAsync()
    {
        Db.Add(Connection);
        await Db.SaveChangesAsync();
        Route = new ConnectionRoute { Revision = Guid.NewGuid().ToString("N"), Fingerprint = ConnectionRouteFingerprint.Create(Connection) };
        Events.Clear();
    }

    internal Task<IResult> UpdateAsync(UpdateConnectionRequest request) => InvokeAsync("UpdateConnection", Connection.Id,
        request, _identity, Db, Sync.Object, null!, NullLogger<SaveConnectionRequest>.Instance, Store.Object);

    internal Task<IResult> UpdateAsync() => UpdateAsync(
        new UpdateConnectionRequest(null, "new-host", null, null, null, "fresh-password", null));

    internal Task<IResult> CreateAsync() => SaveAsync(new SaveConnectionRequest("ignored", "ignored", "new",
        "host", 1433, "db", "reader", "fresh-password", true));

    internal Task<IResult> SaveAsync(SaveConnectionRequest request) => InvokeAsync("SaveConnection", request,
        _identity, Db, Sync.Object, null!, NullLogger<SaveConnectionRequest>.Instance, Store.Object);

    internal Task<IResult> GetAsync(bool list = false) => list
        ? InvokeAsync("GetConnections", _identity, Db, NullLogger<SaveConnectionRequest>.Instance, Store.Object)
        : InvokeAsync("GetConnectionById", Connection.Id, _identity, Db, NullLogger<SaveConnectionRequest>.Instance, Store.Object);

    private static Task<IResult> InvokeAsync(string name, params object[] arguments) =>
        (Task<IResult>)typeof(SavedConnectionEndpoints).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, arguments)!;

    public ValueTask DisposeAsync() => Db.DisposeAsync();

    private sealed class SaveObserver(ConnectionRouteReviewHarness harness) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            harness.Events.Add("sql");
            if (harness.FailSql) throw new InvalidOperationException("private-sql-detail");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (harness.FailAfterSql) throw new InvalidOperationException("private-commit-detail");
            return ValueTask.FromResult(result);
        }
    }
}
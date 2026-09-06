using System.Reflection;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class SavedConnectionBridgeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAndUpdateRejectForeignBridgeBeforeChangingConnection(bool update)
    {
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", "test-only-connection-encryption-key");
        var harness = new BridgeRoutingHarness();
        harness.Store.CallBase = true;
        await using var db = new DataAnalysisDbContext(new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        if (update)
        {
            db.Add(harness.Saved);
            await db.SaveChangesAsync();
        }
        var identity = new Mock<IIdentityService>();
        identity.SetupGet(value => value.CurrentCompanyId).Returns(Guid.Parse(BridgeRoutingHarness.Company));
        identity.SetupGet(value => value.UserId).Returns(Guid.NewGuid());
        var foreignBridge = Guid.NewGuid();
        harness.Store.Setup(store => store.FindAsync(foreignBridge, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisteredBridge?)null);
        object[] arguments = update
            ? [harness.Saved.Id, new UpdateConnectionRequest("changed", "changed-host", null, null, null, null, null,
                ConnectionRoute.Bridge, foreignBridge), identity.Object, db, null!, null!,
                NullLogger<SaveConnectionRequest>.Instance, harness.Store.Object]
            : [new SaveConnectionRequest("untrusted-user", "untrusted-company", "new", "host", 1433, "db", "reader",
                "test-only-password", true, ConnectionRoute.Bridge, foreignBridge), identity.Object, db, null!, null!,
                NullLogger<SaveConnectionRequest>.Instance, harness.Store.Object];

        var method = typeof(SavedConnectionEndpoints).GetMethod(update ? "UpdateConnection" : "SaveConnection",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = await (Task<IResult>)method.Invoke(null, arguments)!;

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        if (update)
        {
            Assert.Equal("customer-pc", harness.Saved.Host);
            Assert.Equal(EntityState.Unchanged, db.Entry(harness.Saved).State);
        }
        else
        {
            Assert.Empty(await db.SavedConnections.ToListAsync());
            Assert.Empty(db.ChangeTracker.Entries());
        }
    }
}
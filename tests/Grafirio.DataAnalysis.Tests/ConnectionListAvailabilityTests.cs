using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using Moq;
using Npgsql;

namespace Grafirio.DataAnalysis.Tests;

public sealed class ConnectionListAvailabilityTests
{
    [Fact]
    public async Task RouteStoreOutageStillListsConnectionsAndStopsQueryingTheStore()
    {
        await using var harness = new ConnectionRouteReviewHarness();
        await harness.SeedAsync();
        harness.Db.Add(new SavedConnection { Id = Guid.NewGuid(), CompanyId = harness.Connection.CompanyId,
            Name = "second", Host = "second-host", CreatedAt = DateTime.UtcNow.AddMinutes(-1) });
        await harness.Db.SaveChangesAsync();
        harness.Store.Setup(store => store.GetConnectionRouteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>())).ThrowsAsync(new MongoException("store down"));

        var list = await harness.GetAsync(list: true);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)list).StatusCode);
        var rows = JsonSerializer.SerializeToElement(((IValueHttpResult)list).Value).GetProperty("connections")
            .EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.False(row.GetProperty("routeAvailable").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("routeError").GetString()));
        });
        harness.Store.Verify(store => store.GetConnectionRouteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WrappedStoreExceptionsAreAttributedToTheFailingComponent()
    {
        Assert.Equal(CatalogFailure.PostgresComponent,
            CatalogFailure.FindComponent(new InvalidOperationException("retries exhausted", new NpgsqlException("refused"))));
        Assert.Equal(CatalogFailure.MongoComponent, CatalogFailure.FindComponent(new MongoException("down")));
        Assert.Null(CatalogFailure.FindComponent(new InvalidOperationException("unrelated")));
    }
}

using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class ConnectionRouteReservationTests
{
    [Fact]
    public async Task ReservationUsesCompareAndSwapAndKeepsPreviousBridgeWhilePending()
    {
        BsonDocument? filterDocument = null;
        BsonDocument? updateDocument = null;
        var collection = new Mock<IMongoCollection<BsonDocument>>();
        collection.Setup(value => value.UpdateOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<UpdateDefinition<BsonDocument>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<BsonDocument>, UpdateDefinition<BsonDocument>, UpdateOptions, CancellationToken>(
                (filter, update, options, _) =>
                {
                    var serializer = BsonSerializer.SerializerRegistry;
                    var args = new RenderArgs<BsonDocument>(serializer.GetSerializer<BsonDocument>(), serializer);
                    filterDocument = filter.Render(args);
                    updateDocument = update.Render(args).AsBsonDocument;
                    Assert.False(options.IsUpsert);
                })
            .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
        var database = new Mock<IMongoDatabase>();
        database.Setup(value => value.GetCollection<BsonDocument>(BridgeStore.RouteCollectionName, null))
            .Returns(collection.Object);
        var store = new BridgeStore(database.Object, NullLogger<BridgeStore>.Instance);
        var previous = new ConnectionRoute(ConnectionRoute.Bridge, Guid.NewGuid())
        {
            Revision = "old-revision", Fingerprint = "old-fingerprint"
        };
        await store.ReserveConnectionRouteAsync(Guid.NewGuid(), "company", previous, "new-revision");
        Assert.Equal("old-revision", filterDocument!["revision"].AsString);
        Assert.Equal("old-fingerprint", filterDocument["fingerprint"].AsString);
        Assert.True(filterDocument["pending"]["$ne"].AsBoolean);
        var values = updateDocument!["$set"].AsBsonDocument;
        Assert.True(values["pending"].AsBoolean);
        Assert.Equal(previous.BridgeId.ToString(), values["bridgeId"].AsString);
        Assert.Equal("new-revision", values["revision"].AsString);
        Assert.Equal("old-fingerprint", values["fingerprint"].AsString);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostReservationOrPublicationCannotOverwriteAnotherWriter(bool publish)
    {
        var collection = new Mock<IMongoCollection<BsonDocument>>();
        collection.Setup(value => value.UpdateOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<UpdateDefinition<BsonDocument>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));
        var database = new Mock<IMongoDatabase>();
        database.Setup(value => value.GetCollection<BsonDocument>(BridgeStore.RouteCollectionName, null))
            .Returns(collection.Object);
        var store = new BridgeStore(database.Object, NullLogger<BridgeStore>.Instance);
        var route = new ConnectionRoute { Revision = "old", Fingerprint = "fingerprint" };
        await Assert.ThrowsAsync<DataSourceException>(() => publish
            ? store.CompleteConnectionRouteAsync(Guid.NewGuid(), "company", "reservation", route)
            : store.ReserveConnectionRouteAsync(Guid.NewGuid(), "company", route, "reservation"));
    }

    [Fact]
    public async Task PendingReservationIsNeverAutomaticallyRetried()
    {
        var store = new BridgeStore(Mock.Of<IMongoDatabase>(), NullLogger<BridgeStore>.Instance);
        await Assert.ThrowsAsync<DataSourceException>(() => store.ReserveConnectionRouteAsync(
            Guid.NewGuid(), "company", new ConnectionRoute { Pending = true }, "retry"));
    }

    [Fact]
    public async Task FailedPendingRetryUsesCompareAndSwapAndBecomesBusyAgain()
    {
        BsonDocument? filterDocument = null;
        BsonDocument? updateDocument = null;
        var collection = new Mock<IMongoCollection<BsonDocument>>();
        collection.Setup(value => value.UpdateOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<UpdateDefinition<BsonDocument>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<BsonDocument>, UpdateDefinition<BsonDocument>, UpdateOptions, CancellationToken>(
                (filter, update, options, _) =>
                {
                    var serializer = BsonSerializer.SerializerRegistry;
                    var args = new RenderArgs<BsonDocument>(serializer.GetSerializer<BsonDocument>(), serializer);
                    filterDocument = filter.Render(args);
                    updateDocument = update.Render(args).AsBsonDocument;
                    Assert.False(options.IsUpsert);
                })
            .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
        var database = new Mock<IMongoDatabase>();
        database.Setup(value => value.GetCollection<BsonDocument>(BridgeStore.RouteCollectionName, null))
            .Returns(collection.Object);
        var store = new BridgeStore(database.Object, NullLogger<BridgeStore>.Instance);
        var previous = new ConnectionRoute(ConnectionRoute.Bridge, Guid.NewGuid())
        {
            Revision = "failed-revision", Fingerprint = "old-fingerprint", Pending = true, Failed = true
        };
        await store.ReserveConnectionRouteAsync(Guid.NewGuid(), "company", previous, "retry-revision");
        Assert.Equal("failed-revision", filterDocument!["revision"].AsString);
        Assert.Equal("old-fingerprint", filterDocument["fingerprint"].AsString);
        Assert.True(filterDocument["pending"].AsBoolean);
        Assert.True(filterDocument["failed"].AsBoolean);
        var values = updateDocument!["$set"].AsBsonDocument;
        Assert.True(values["pending"].AsBoolean);
        Assert.False(values["failed"].AsBoolean);
        Assert.Equal("retry-revision", values["revision"].AsString);
        Assert.Equal(previous.BridgeId.ToString(), values["bridgeId"].AsString);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailureMarkerCannotReleaseAnotherWriterOrAnAlreadyPublishedRoute(bool matched)
    {
        BsonDocument? filterDocument = null;
        BsonDocument? updateDocument = null;
        var collection = new Mock<IMongoCollection<BsonDocument>>();
        collection.Setup(value => value.UpdateOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<UpdateDefinition<BsonDocument>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<BsonDocument>, UpdateDefinition<BsonDocument>, UpdateOptions, CancellationToken>(
                (filter, update, _, _) =>
                {
                    var registry = BsonSerializer.SerializerRegistry;
                    var args = new RenderArgs<BsonDocument>(registry.GetSerializer<BsonDocument>(), registry);
                    filterDocument = filter.Render(args);
                    updateDocument = update.Render(args).AsBsonDocument;
                })
            .ReturnsAsync(new UpdateResult.Acknowledged(matched ? 1 : 0, matched ? 1 : 0, null));
        var database = new Mock<IMongoDatabase>();
        database.Setup(value => value.GetCollection<BsonDocument>(BridgeStore.RouteCollectionName, null))
            .Returns(collection.Object);
        var store = new BridgeStore(database.Object, NullLogger<BridgeStore>.Instance);
        if (matched)
            await store.FailConnectionRouteAsync(Guid.NewGuid(), "company", "own-revision");
        else
            await Assert.ThrowsAsync<DataSourceException>(() =>
                store.FailConnectionRouteAsync(Guid.NewGuid(), "company", "own-revision"));
        Assert.Equal("own-revision", filterDocument!["revision"].AsString);
        Assert.True(filterDocument["pending"].AsBoolean);
        Assert.True(filterDocument["failed"]["$ne"].AsBoolean);
        Assert.Equal(new BsonDocument("$set", new BsonDocument("failed", true)), updateDocument);
    }
}
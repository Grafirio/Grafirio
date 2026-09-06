using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

internal sealed class BridgeRoutingHarness
{
    internal const string Company = "11111111-1111-1111-1111-111111111111";
    internal static readonly Guid SelectedBridge = Guid.Parse("33333333-3333-3333-3333-333333333333");
    internal static readonly Guid OtherBridge = Guid.Parse("44444444-4444-4444-4444-444444444444");
    internal Mock<BridgeStore> Store { get; } = new(Mock.Of<IMongoDatabase>(), NullLogger<BridgeStore>.Instance);
    internal Mock<IHubContext<BridgeHub>> Hub { get; } = new(MockBehavior.Strict);
    internal Mock<ISingleClientProxy> Client { get; } = new(MockBehavior.Strict);
    internal BridgeRegistry Registry { get; }
    internal DataSourceFactory Factory { get; }
    internal ConnectionProfileStore Profiles { get; }
    internal List<(string Method, object Payload)> Messages { get; } = [];
    internal bool FailConfiguration { get; set; }
    internal bool CompleteQueries { get; set; } = true;
    internal ConnectionRoute Route { get; set; } = new(ConnectionRoute.Bridge, SelectedBridge);
    internal SavedConnection Saved { get; } = new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, Host = "customer-pc", Port = 1433,
        Database = "customer", Username = "reader", Name = "customer database",
        EncryptedPassword = "invalid-ciphertext-detects-unwanted-direct-access"
    };

    internal BridgeRoutingHarness()
    {
        BindRoute();
        Store.Setup(store => store.GetConnectionRouteAsync(Saved.Id, Company, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Route);
        Store.Setup(store => store.FindAsync(SelectedBridge, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisteredBridge(SelectedBridge, Company, "selected", "pc", "1", DateTime.UtcNow, null));
        var responseBus = new Mock<IBridgeResponseBus>();
        Func<BridgeResponse, CancellationToken, Task> deliver = null!;
        responseBus.Setup(bus => bus.OnLocalDelivery(It.IsAny<Func<BridgeResponse, CancellationToken, Task>>()))
            .Callback<Func<BridgeResponse, CancellationToken, Task>>(handler => deliver = handler);
        responseBus.Setup(bus => bus.DispatchAsync(It.IsAny<BridgeResponse>(), It.IsAny<CancellationToken>()))
            .Returns<BridgeResponse, CancellationToken>((response, ct) => deliver(response, ct));
        Registry = new BridgeRegistry(responseBus.Object, NullLogger<BridgeRegistry>.Instance);
        Registry.Start();
        Registry.Attach(OtherBridge, "other-client", Company, "1");
        Registry.Attach(SelectedBridge, "selected-client", Company, "1");
        var clients = new Mock<IHubClients>(MockBehavior.Strict);
        clients.Setup(value => value.Client("selected-client")).Returns(Client.Object);
        Hub.SetupGet(hub => hub.Clients).Returns(clients.Object);
        Client.Setup(client => client.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns<string, object?[], CancellationToken>(async (method, arguments, ct) =>
            {
                Messages.Add((method, arguments[0]!));
                if (method == BridgeProtocol.ServerToBridge.ConfigureConnection && FailConfiguration)
                    throw new InvalidOperationException("configuration delivery failed");
                if (arguments[0] is ExecuteQueryRequest query && CompleteQueries)
                {
                    await Registry.DispatchAsync(new BridgeResponse(query.RequestId, Chunk: new QueryChunk(
                        query.RequestId, 0, [new QueryColumn("value", SqlValueKind.Integer)], [["1"]])), ct);
                    await Registry.DispatchAsync(new BridgeResponse(query.RequestId,
                        Completed: new QueryCompleted(query.RequestId, 1, false, [])), ct);
                }
            });
        var collection = new Mock<IMongoCollection<BsonDocument>>();
        collection.Setup(value => value.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var cursor = new Mock<IAsyncCursor<BsonDocument>>();
                cursor.SetupGet(value => value.Current).Returns([new BsonDocument("selectedTables", new BsonArray { "dbo.Orders" })]);
                cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true).ReturnsAsync(false);
                return Task.FromResult(cursor.Object);
            });
        var mongo = new Mock<IMongoDatabase>();
        mongo.Setup(database => database.GetCollection<BsonDocument>(ConnectionProfileStore.CollectionName, null))
            .Returns(collection.Object);
        Profiles = new ConnectionProfileStore(mongo.Object, NullLogger<ConnectionProfileStore>.Instance);
        Factory = new DataSourceFactory(Store.Object, Registry, Hub.Object, NullLoggerFactory.Instance, Profiles);
    }

    internal void BindRoute() => Route = Route with
    {
        Fingerprint = ConnectionRouteFingerprint.Create(Saved), Revision = Guid.NewGuid().ToString("N")
    };

    internal DataSourceTarget Preview => new("customer-pc", 1433, "customer", "reader", "test-only-password", true)
    {
        CompanyId = Company, Route = Route
    };
}
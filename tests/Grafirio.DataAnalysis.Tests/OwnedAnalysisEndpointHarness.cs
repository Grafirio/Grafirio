using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.Shared.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

internal sealed class OwnedAnalysisEndpointHarness : IAsyncDisposable
{
    internal const string OrdersTable = "dbo.Orders";
    internal const string CustomersTable = "dbo.Customers";
    internal DataAnalysisDbContext Db { get; } = new(new DbContextOptionsBuilder<DataAnalysisDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    internal Mock<IIdentityService> Identity { get; } = new(MockBehavior.Strict);
    internal Mock<IMongoCollection<BsonDocument>> Collection { get; } = new(MockBehavior.Strict);
    internal ConnectionProfileStore Profiles { get; }
    internal SavedConnection Connection { get; }
    internal AnalysisConfig Config { get; }
    internal string[] SelectedTables { get; } = [OrdersTable, CustomersTable];

    internal OwnedAnalysisEndpointHarness()
    {
        var companyId = Guid.NewGuid();
        Identity.SetupGet(value => value.CurrentCompanyId).Returns(companyId);
        Connection = new SavedConnection
        {
            Id = Guid.NewGuid(), CompanyId = companyId.ToString(), IsActive = true
        };
        Config = new AnalysisConfig
        {
            Id = Guid.NewGuid(), ConnectionId = Connection.Id, CompanyId = Connection.CompanyId,
            IsActive = true, Status = "ready", TablesJson = JsonSerializer.Serialize(SelectedTables)
        };
        Db.AddRange(Connection, Config);

        var database = new Mock<IMongoDatabase>(MockBehavior.Strict);
        database.Setup(value => value.GetCollection<BsonDocument>(ConnectionProfileStore.CollectionName, null))
            .Returns(Collection.Object);
        Profiles = new ConnectionProfileStore(database.Object, NullLogger<ConnectionProfileStore>.Instance);
        Collection.Setup(value => value.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<BsonDocument>, FindOptions<BsonDocument, BsonDocument>, CancellationToken>(
                (filter, _, _) =>
                {
                    var registry = BsonSerializer.SerializerRegistry;
                    var rendered = filter.Render(new RenderArgs<BsonDocument>(registry.GetSerializer<BsonDocument>(), registry));
                    Assert.Equal(Connection.Id.ToString(), rendered["_id"].AsString);
                    Assert.Equal(Connection.CompanyId, rendered["companyId"].AsString);
                })
            .Returns(() =>
            {
                var cursor = new Mock<IAsyncCursor<BsonDocument>>(MockBehavior.Strict);
                cursor.SetupGet(value => value.Current).Returns(
                    [new BsonDocument("selectedTables", new BsonArray(SelectedTables))]);
                cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true).ReturnsAsync(false);
                cursor.Setup(value => value.Dispose());
                return Task.FromResult(cursor.Object);
            });
    }

    internal void VerifySelectionRead() => Collection.Verify(value => value.FindAsync(
        It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
        It.IsAny<CancellationToken>()), Times.Once);

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
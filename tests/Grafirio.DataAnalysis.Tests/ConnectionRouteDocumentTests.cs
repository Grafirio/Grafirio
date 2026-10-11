using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using MongoDB.Bson;

namespace Grafirio.DataAnalysis.Tests;

public sealed class ConnectionRouteDocumentTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{connectionMode:'bridge',bridgeId:'not-a-guid'}")]
    [InlineData("{connectionMode:'direct',bridgeId:null,pending:'yes'}")]
    [InlineData("{connectionMode:42,bridgeId:null}")]
    public void CorruptDocumentProducesUnavailableRouteNotDirectFallback(string json)
    {
        var route = BridgeStore.ReadRoute(BsonDocument.Parse(json));
        Assert.NotEqual(ConnectionRoute.Direct, route.ConnectionMode);
        Assert.Throws<DataSourceException>(() => route.Validate());
    }

    [Fact]
    public void ValidDirectDocumentRemainsReadable()
    {
        var route = BridgeStore.ReadRoute(BsonDocument.Parse("{connectionMode:'direct',bridgeId:null}"));
        Assert.Equal(ConnectionRoute.Direct, route.ConnectionMode);
    }
}

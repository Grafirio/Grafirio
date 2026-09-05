using System.Text;
using System.Text.Json;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

internal sealed class InternalDataEndpointHarness : IAsyncDisposable
{
    internal const string ApiKey = "test-only-internal-key";
    internal const string QueryPath = "/internal/data/query";
    internal const string AllowedSql = "SELECT Id FROM dbo.Orders WHERE Id > @minimum";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebApplication _application;
    private readonly IServiceScope _scope;
    internal Mock<IDataSourceFactory> DataSources { get; } = new(MockBehavior.Strict);
    internal Mock<IDataSourceSession> Session { get; } = new(MockBehavior.Strict);
    internal Mock<IMongoCollection<BsonDocument>> Collection { get; } = new(MockBehavior.Strict);
    internal DataAnalysisDbContext Db { get; }
    internal IConfiguration Configuration => _application.Configuration;
    internal string[] SelectedTables { get; set; } = ["dbo.Orders"];
    internal AnalysisConfig Config { get; } = new()
    {
        Id = Guid.NewGuid(), CompanyId = "company-a", ConnectionId = Guid.NewGuid(),
        Status = "ready", IsActive = true, ConfigJson = "{\"label\":\"İstanbul\"}", TablesJson = "[\"dbo.Orders\"]"
    };
    internal QueryHistory Query { get; }
    internal SavedConnection Connection { get; }
    internal InternalQueryRequest Request => new(Connection.Id, Config.CompanyId, Query.Id, Config.Id,
        InternalQueryScope.ComputeConfigHash(Config.ConfigJson), AllowedSql);

    internal InternalDataEndpointHarness()
    {
        Query = new QueryHistory { Id = Guid.NewGuid(), ConfigId = Config.Id, Status = "processing" };
        Connection = new SavedConnection { Id = Config.ConnectionId, CompanyId = Config.CompanyId, IsActive = true };
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        // Build requires a server registration; no listener is started for request-delegate tests.
        builder.WebHost.UseKestrelCore();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Internal:ApiKey"] = ApiKey });
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<DataAnalysisDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton(DataSources.Object);
        var database = new Mock<IMongoDatabase>(MockBehavior.Strict);
        database.Setup(value => value.GetCollection<BsonDocument>(ConnectionProfileStore.CollectionName, null))
            .Returns(Collection.Object);
        builder.Services.AddSingleton(database.Object);
        builder.Services.AddSingleton<ConnectionProfileStore>();
        ConfigureSelectionRead();
        _application = builder.Build();
        _application.MapInternalDataEndpoints();
        _scope = _application.Services.CreateScope();
        Db = _scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();
        Db.AddRange(Query, Config, Connection);
    }

    private void ConfigureSelectionRead()
    {
        Collection.Setup(value => value.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<BsonDocument>, FindOptions<BsonDocument, BsonDocument>, CancellationToken>(
                (filter, _, _) =>
                {
                    var registry = BsonSerializer.SerializerRegistry;
                    var rendered = filter.Render(new RenderArgs<BsonDocument>(registry.GetSerializer<BsonDocument>(), registry));
                    Assert.Equal(Connection.Id.ToString(), rendered["_id"].AsString);
                    Assert.Equal(Config.CompanyId, rendered["companyId"].AsString);
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

    internal void AllowSession()
    {
        DataSources.Setup(value => value.OpenAsync(It.Is<SavedConnection>(connection =>
                connection.Id == Connection.Id && connection.CompanyId == Config.CompanyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Session.Object);
        Session.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
    }

    internal Task<DefaultHttpContext> SendAsync(InternalQueryRequest? request = null, string? apiKey = ApiKey) =>
        SendJsonAsync(JsonSerializer.Serialize(request ?? Request, JsonOptions), apiKey);

    internal async Task<DefaultHttpContext> SendJsonAsync(string json, string? apiKey = ApiKey)
    {
        await Db.SaveChangesAsync();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)_application).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
            value => value.RoutePattern.RawText == QueryPath);
        Assert.Equal([HttpMethods.Post], endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods);
        var context = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = QueryPath;
        context.Request.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
        if (apiKey is not null) context.Request.Headers[InternalDataEndpoints.ApiKeyHeader] = apiKey;
        var bodyFeature = new Mock<IHttpRequestBodyDetectionFeature>();
        bodyFeature.SetupGet(value => value.CanHaveBody).Returns(true);
        context.Features.Set(bodyFeature.Object);
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        context.Response.Body.Position = 0;
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _application.DisposeAsync();
    }
}
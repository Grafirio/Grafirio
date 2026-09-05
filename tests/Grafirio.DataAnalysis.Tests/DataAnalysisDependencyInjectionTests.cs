using Grafirio.DataAnalysis.Api.Application.Interfaces;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.DataAnalysis.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class DataAnalysisDependencyInjectionTests
{
    [Fact]
    public void ProductionRegistrationShapesResolveInterfacesWithoutConstructorAmbiguity()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Internal:ApiKey"] = " " }).Build();
        var database = new Mock<IMongoDatabase>(MockBehavior.Strict);
        database.Setup(value => value.GetCollection<BsonDocument>(LearnedFactStore.CollectionName, null))
            .Returns(new Mock<IMongoCollection<BsonDocument>>(MockBehavior.Strict).Object);
        var dataSources = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<DataAnalysisDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton(database.Object);
        services.AddSingleton(dataSources.Object);
        services.AddSingleton<LearnedFactStore>();
        services.AddSingleton<RelationshipDiscovery>();
        services.AddHttpClient<IPyCaretClient, PyCaretClient>();
        services.AddScoped<IPyCaretQueryService, PyCaretQueryService>();
        services.AddScoped<IRelationshipApprovalService, RelationshipApprovalService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var client = firstScope.ServiceProvider.GetRequiredService<IPyCaretClient>();
        var queryService = firstScope.ServiceProvider.GetRequiredService<IPyCaretQueryService>();
        var approvalService = firstScope.ServiceProvider.GetRequiredService<IRelationshipApprovalService>();

        Assert.IsType<PyCaretClient>(client);
        Assert.False(client.IsConfigured);
        Assert.IsType<PyCaretQueryService>(queryService);
        Assert.False(queryService.IsConfigured);
        Assert.IsType<RelationshipApprovalService>(approvalService);
        Assert.Null(provider.GetService<IRelationshipDecisionPersistence>());
        Assert.Same(queryService, firstScope.ServiceProvider.GetRequiredService<IPyCaretQueryService>());
        Assert.Same(approvalService, firstScope.ServiceProvider.GetRequiredService<IRelationshipApprovalService>());
        Assert.NotSame(queryService, secondScope.ServiceProvider.GetRequiredService<IPyCaretQueryService>());
        Assert.NotSame(approvalService, secondScope.ServiceProvider.GetRequiredService<IRelationshipApprovalService>());
        dataSources.VerifyNoOtherCalls();
        database.Verify(value => value.GetCollection<BsonDocument>(LearnedFactStore.CollectionName, null), Times.Once);
        database.VerifyNoOtherCalls();
    }
}
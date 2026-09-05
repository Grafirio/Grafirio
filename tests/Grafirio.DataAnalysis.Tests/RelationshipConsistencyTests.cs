using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class RelationshipConsistencyTests
{
    private const string CompanyId = "company";
    private const string OtherCompanyId = "other-company";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Apply_AcceptanceValidatesLiveDataBeforeWritingAndSurvivesRefresh()
    {
        var persistence = new FakePersistence();
        var session = new FakeDataSourceSession()
            .Respond("INFORMATION_SCHEMA.COLUMNS",
                FakeDataSourceSession.Row(("Schema", "dbo"), ("TableName", "Orders"), ("ColumnName", "CustomerId"), ("DataType", "int")),
                FakeDataSourceSession.Row(("Schema", "dbo"), ("TableName", "Customers"), ("ColumnName", "Id"), ("DataType", "int")))
            .Respond("sys.indexes")
            .Respond("AS HasDuplicates", FakeDataSourceSession.Row(("HasDuplicates", false)))
            .Respond("AS Matched", FakeDataSourceSession.Row(("Total", 100), ("Matched", 100)));
        var dataSources = new Mock<IDataSourceFactory>(MockBehavior.Strict);
        dataSources.Setup(factory => factory.OpenAsync(persistence.Connection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var service = new RelationshipApprovalService(persistence, dataSources.Object,
            new RelationshipDiscovery(NullLogger<RelationshipDiscovery>.Instance),
            NullLogger<RelationshipApprovalService>.Instance);

        var result = await service.ApplyAsync(persistence.Connection.Id, CompanyId, Fact(true), default);

        Assert.True(result.Applied);
        Assert.Equal(4, session.ExecutedQueries.Count);
        Assert.Equal(["save", "cas"], persistence.Operations);
        var refreshed = await persistence.GetActiveConfigAsync(persistence.Connection.Id, CompanyId, default);
        Assert.True(RelationshipDictionary.AppliedDecisions(refreshed!.ConfigJson)[Fact(true).Key]);
    }

    [Theory]
    [InlineData("ownership")]
    [InlineData("inactive")]
    [InlineData("tables")]
    [InlineData("dictionary")]
    [InlineData("version")]
    [InlineData("status")]
    public async Task Forget_ConcurrentSnapshotChangesDoNotGetOverwritten(string change)
    {
        var persistence = new FakePersistence();
        persistence.Config!.ConfigJson = RelationshipDictionary.Apply("{}", Fact(true), Edge());
        persistence.BeforeDelete = () =>
        {
            switch (change)
            {
                case "ownership": persistence.Connection.CompanyId = OtherCompanyId; break;
                case "inactive": persistence.Config.IsActive = false; break;
                case "tables": persistence.Config.TablesJson = "[]"; break;
                case "dictionary": persistence.Config.ConfigJson = "{\"concurrent\":true}"; break;
                case "version": persistence.Config.UpdatedAt = DateTime.UtcNow; break;
                case "status": persistence.Config.Status = AgentAnalyzeEndpoints.AnalysisStatus.Analyzing; break;
            }
        };

        var result = await Service(persistence).ForgetAsync(persistence.Connection.Id, CompanyId, Fact(true).Key, default);

        Assert.False(result.Applied);
        Assert.Equal(409, result.StatusCode);
        if (change == "dictionary") Assert.Equal("{\"concurrent\":true}", persistence.Config.ConfigJson);
        else Assert.True(RelationshipDictionary.AppliedDecisions(persistence.Config.ConfigJson)[Fact(true).Key]);
    }

    [Fact]
    public async Task Apply_MongoFailurePropagatesWithoutUpdatingActiveDictionary()
    {
        var persistence = new FakePersistence { MongoFailure = new InvalidOperationException("Mongo failed") };
        var before = persistence.Config!.ConfigJson;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(persistence)
            .ApplyAsync(persistence.Connection.Id, CompanyId, Fact(false), default));

        Assert.Same(persistence.MongoFailure, error);
        Assert.Equal(before, persistence.Config.ConfigJson);
        Assert.Equal(["save"], persistence.Operations);
    }

    [Fact]
    public async Task Apply_SqlFailurePropagatesAndRetainsCompleteFactForRetry()
    {
        var persistence = new FakePersistence { SqlFailure = new InvalidOperationException("SQL failed") };
        var fact = Fact(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(persistence)
            .ApplyAsync(persistence.Connection.Id, CompanyId, fact, default));

        Assert.Same(fact, persistence.Facts[fact.Key]);
        Assert.Equal("Which reference?", persistence.Facts[fact.Key].Question);
        Assert.Equal("user", persistence.Facts[fact.Key].UserId);
        Assert.Empty(RelationshipDictionary.AppliedDecisions(persistence.Config!.ConfigJson));
        Assert.Equal(["save", "cas"], persistence.Operations);
    }

    [Fact]
    public async Task Apply_ConflictIsNotSuccessAndRetryIsIdempotent()
    {
        var persistence = new FakePersistence { Conflict = true };
        var service = Service(persistence);
        var fact = Fact(false);

        var conflict = await service.ApplyAsync(persistence.Connection.Id, CompanyId, fact, default);

        Assert.False(conflict.Applied);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Contains("kaydedildi", conflict.Error);
        Assert.Same(fact, Assert.Single(persistence.Facts).Value);
        Assert.Empty(RelationshipDictionary.AppliedDecisions(persistence.Config!.ConfigJson));

        persistence.Conflict = false;
        Assert.True((await service.ApplyAsync(persistence.Connection.Id, CompanyId, fact, default)).Applied);
        var afterFirstSuccess = persistence.Config.ConfigJson;
        Assert.True((await service.ApplyAsync(persistence.Connection.Id, CompanyId, fact, default)).Applied);
        Assert.Equal(afterFirstSuccess, persistence.Config.ConfigJson);
        Assert.Single(persistence.Facts);
        Assert.False(RelationshipDictionary.AppliedDecisions(persistence.Config.ConfigJson)[fact.Key]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WrongCompanyCannotApplyOrForget(bool forget)
    {
        var persistence = new FakePersistence();
        var service = Service(persistence);
        var result = forget
            ? await service.ForgetAsync(persistence.Connection.Id, OtherCompanyId, Fact(true).Key, default)
            : await service.ApplyAsync(persistence.Connection.Id, OtherCompanyId, Fact(false), default);

        Assert.False(result.Applied);
        Assert.Equal(404, result.StatusCode);
        Assert.Empty(persistence.Operations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Forget_CleansActiveDecisionEvenWhenMongoFactIsAlreadyMissing(bool accepted)
    {
        var persistence = new FakePersistence();
        persistence.Config!.ConfigJson = RelationshipDictionary.Apply("{}", Fact(accepted), accepted ? Edge() : null);
        var service = Service(persistence);

        Assert.True((await service.ForgetAsync(persistence.Connection.Id, CompanyId, Fact(accepted).Key, default)).Applied);

        Assert.True(persistence.Config.IsActive);
        Assert.Empty(RelationshipDictionary.AppliedDecisions(persistence.Config.ConfigJson));
        Assert.Empty(JsonNode.Parse(persistence.Config.ConfigJson)!["relationships"]!.AsArray());
        var after = persistence.Config.ConfigJson;
        Assert.True((await service.ForgetAsync(persistence.Connection.Id, CompanyId, Fact(accepted).Key, default)).Applied);
        Assert.Equal(after, persistence.Config.ConfigJson);
        Assert.Equal(["delete", "cas", "delete", "cas"], persistence.Operations);
    }

    [Fact]
    public async Task Forget_ConflictAfterMongoDeleteCanBeRetried()
    {
        var persistence = new FakePersistence { Conflict = true };
        var fact = Fact(true);
        persistence.Facts[fact.Key] = fact;
        persistence.Config!.ConfigJson = RelationshipDictionary.Apply("{}", fact, Edge());
        var service = Service(persistence);

        var result = await service.ForgetAsync(persistence.Connection.Id, CompanyId, fact.Key, default);

        Assert.False(result.Applied);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(persistence.Facts);
        Assert.True(RelationshipDictionary.AppliedDecisions(persistence.Config.ConfigJson)[fact.Key]);

        persistence.Conflict = false;
        Assert.True((await service.ForgetAsync(persistence.Connection.Id, CompanyId, fact.Key, default)).Applied);
        Assert.Empty(RelationshipDictionary.AppliedDecisions(persistence.Config.ConfigJson));
    }

    [Fact]
    public async Task Forget_MongoFailureDoesNotTouchDictionary()
    {
        var persistence = new FakePersistence { MongoFailure = new InvalidOperationException("Mongo failed") };
        persistence.Config!.ConfigJson = RelationshipDictionary.Apply("{}", Fact(true), Edge());
        var before = persistence.Config.ConfigJson;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(persistence)
            .ForgetAsync(persistence.Connection.Id, CompanyId, Fact(true).Key, default));

        Assert.Equal(before, persistence.Config.ConfigJson);
        Assert.Equal(["delete"], persistence.Operations);
    }

    [Fact]
    public async Task Forget_SqlFailureDoesNotReportSuccessAndRetryStillCleansDictionary()
    {
        var persistence = new FakePersistence { SqlFailure = new InvalidOperationException("SQL failed") };
        persistence.Config!.ConfigJson = RelationshipDictionary.Apply("{}", Fact(true), Edge());
        var service = Service(persistence);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service
            .ForgetAsync(persistence.Connection.Id, CompanyId, Fact(true).Key, default));
        Assert.True(RelationshipDictionary.AppliedDecisions(persistence.Config.ConfigJson)[Fact(true).Key]);

        persistence.SqlFailure = null;
        Assert.True((await service.ForgetAsync(persistence.Connection.Id, CompanyId, Fact(true).Key, default)).Applied);
        Assert.Empty(RelationshipDictionary.AppliedDecisions(persistence.Config.ConfigJson));
    }

    [Theory]
    [InlineData("syn:dbo.orders.total=amount")]
    [InlineData("mean:dbo.orders.total")]
    [InlineData("code:dbo.orders.status=open")]
    [InlineData("label:dbo.orders")]
    public async Task Forget_OtherKindsInvalidateActiveDictionaryEvenOnRetryWithoutMongoRecord(string key)
    {
        var persistence = new FakePersistence();
        var config = persistence.Config!;
        var service = Service(persistence);

        Assert.True((await service.ForgetAsync(persistence.Connection.Id, CompanyId, key, default)).Applied);
        Assert.False(config.IsActive);
        Assert.True((await service.ForgetAsync(persistence.Connection.Id, CompanyId, key, default)).Applied);
    }

    [Theory]
    [InlineData("analyzing")]
    [InlineData("failed")]
    public async Task Forget_InvalidatesUnfinishedAnalysisSoLoadedFactsCannotBePublished(string status)
    {
        var persistence = new FakePersistence();
        persistence.Config!.Status = status;
        Assert.True((await Service(persistence).ForgetAsync(persistence.Connection.Id, CompanyId, Fact(true).Key, default)).Applied);
        Assert.False(persistence.Config.IsActive);
    }

    [Fact]
    public async Task Forget_UnknownKeyDoesNotMutateEitherStore()
    {
        var persistence = new FakePersistence();
        var result = await Service(persistence).ForgetAsync(persistence.Connection.Id, CompanyId, "invalid", default);
        Assert.Equal(400, result.StatusCode);
        Assert.Empty(persistence.Operations);
    }

    [Fact]
    public async Task Forget_AnalysisAppearingAfterInitialReadReportsConflict()
    {
        var persistence = new FakePersistence();
        var config = persistence.Config;
        persistence.Config = null;
        persistence.BeforeDelete = () => persistence.Config = config;

        var result = await Service(persistence).ForgetAsync(persistence.Connection.Id, CompanyId, Fact(true).Key, default);

        Assert.False(result.Applied);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Forget_CancellationPropagatesWithoutSqlWrite()
    {
        using var cancellation = new CancellationTokenSource();
        var persistence = new FakePersistence { BeforeDelete = cancellation.Cancel };

        await Assert.ThrowsAsync<OperationCanceledException>(() => Service(persistence)
            .ForgetAsync(persistence.Connection.Id, CompanyId, Fact(true).Key, cancellation.Token));

        Assert.Equal(["delete"], persistence.Operations);
    }

    [Fact]
    public void Forget_RemovesExactNormalizedDeclaredAndRejectedKeysOnly()
    {
        var exact = Edge();
        exact.FromTable = "[DBO].[ORDERS]";
        exact.FromColumns = ["[CUSTOMERID]"];
        var alternateColumn = Edge();
        alternateColumn.FromColumns = ["CustomerCode"];
        var reverse = Edge();
        reverse.FromTable = "dbo.Customers";
        reverse.FromColumns = ["Id"];
        reverse.ToTable = "dbo.Orders";
        reverse.ToColumns = ["CustomerId"];
        var composite = Edge();
        composite.FromColumns.Add("TenantId");
        composite.ToColumns.Add("TenantId");
        var catalog = Edge();
        catalog.Source = "foreign_key";
        var inferred = Edge();
        inferred.Source = "inferred";
        var unrelatedRejection = new RelationshipProposal("dbo.Orders", "CustomerCode", "dbo.Customers", "Id");
        RelationshipProfile[] preserved = [alternateColumn, reverse, composite, catalog, inferred];
        var dictionary = new JsonObject
        {
            ["relationships"] = JsonSerializer.SerializeToNode(new[] { exact }.Concat(preserved), JsonOptions),
            [RelationshipDictionary.RejectedProperty] = JsonSerializer.SerializeToNode(new[]
            {
                new RelationshipProposal("dbo.Orders", "CustomerId", "dbo.Customers", "Id"), unrelatedRejection
            }, JsonOptions),
            ["profileStats"] = new JsonObject { ["relationshipCount"] = 6, ["inferredRelationshipCount"] = 1, ["tableCount"] = 2 },
            ["custom"] = new JsonObject { ["nested"] = new JsonArray(true, 17) }
        };

        var updated = JsonNode.Parse(RelationshipDictionary.Forget(dictionary.ToJsonString(), Fact(true).Key))!;

        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(preserved, JsonOptions), updated["relationships"]));
        Assert.Equal(unrelatedRejection, Assert.Single(updated[RelationshipDictionary.RejectedProperty]!
            .Deserialize<List<RelationshipProposal>>(JsonOptions)!));
        Assert.False(RelationshipDictionary.AppliedDecisions(updated.ToJsonString()).ContainsKey(Fact(true).Key));
        Assert.Equal(5, updated["profileStats"]!["relationshipCount"]!.GetValue<int>());
        Assert.Equal(1, updated["profileStats"]!["inferredRelationshipCount"]!.GetValue<int>());
        Assert.Equal(2, updated["profileStats"]!["tableCount"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(dictionary["custom"], updated["custom"]));
        Assert.Equal(6, dictionary["relationships"]!.AsArray().Count);
    }

    [Fact]
    public void Forget_NoMatchingKeyPreservesExactJsonAndHashInput()
    {
        const string json = "{ \"custom\": [true, null, 17] }";
        Assert.Equal(json, RelationshipDictionary.Forget(json, Fact(true).Key));
    }

    private static LearnedFact Fact(bool accepted) => LearnedFact.ForRelationship(
        "dbo.Orders", "CustomerId", "dbo.Customers", "Id", accepted, "Which reference?", "user");

    private static RelationshipProfile Edge() => new()
    {
        FromTable = "dbo.Orders", FromColumns = ["CustomerId"], ToTable = "dbo.Customers", ToColumns = ["Id"],
        Source = "declared", NeedsConfirmation = false
    };

    private static RelationshipApprovalService Service(FakePersistence persistence) => new(
        persistence, Mock.Of<IDataSourceFactory>(MockBehavior.Strict),
        new RelationshipDiscovery(NullLogger<RelationshipDiscovery>.Instance),
        NullLogger<RelationshipApprovalService>.Instance);

    private sealed class FakePersistence : IRelationshipDecisionPersistence
    {
        public SavedConnection Connection { get; } = new() { Id = Guid.NewGuid(), CompanyId = CompanyId };
        public AnalysisConfig? Config { get; set; } = new()
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, Status = AgentAnalyzeEndpoints.AnalysisStatus.Ready,
            TablesJson = "[\"dbo.Orders\",\"dbo.Customers\"]"
        };
        public Dictionary<string, LearnedFact> Facts { get; } = [];
        public List<string> Operations { get; } = [];
        public Exception? MongoFailure { get; init; }
        public Exception? SqlFailure { get; set; }
        public bool Conflict { get; set; }
        public Action? BeforeDelete { get; set; }

        public Task<SavedConnection?> GetConnectionAsync(Guid connectionId, string companyId, CancellationToken ct) =>
            Task.FromResult<SavedConnection?>(connectionId == Connection.Id && companyId == Connection.CompanyId
                && Connection.IsActive ? Connection : null);

        public Task<AnalysisConfig?> GetActiveConfigAsync(Guid connectionId, string companyId, CancellationToken ct) =>
            Task.FromResult(Config is { IsActive: true } && connectionId == Connection.Id && companyId == CompanyId
                ? JsonSerializer.Deserialize<AnalysisConfig>(JsonSerializer.Serialize(Config)) : null);

        public Task SaveFactAsync(Guid connectionId, string companyId, LearnedFact fact, CancellationToken ct)
        {
            Operations.Add("save");
            ct.ThrowIfCancellationRequested();
            if (MongoFailure is not null) throw MongoFailure;
            Facts[fact.Key] = fact;
            return Task.CompletedTask;
        }

        public Task DeleteFactAsync(Guid connectionId, string companyId, string key, CancellationToken ct)
        {
            Operations.Add("delete");
            BeforeDelete?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (MongoFailure is not null) throw MongoFailure;
            Facts.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> TryUpdateConfigAsync(AnalysisConfig snapshot, string dictionaryJson, bool isActive, CancellationToken ct)
        {
            Operations.Add("cas");
            ct.ThrowIfCancellationRequested();
            if (SqlFailure is not null) throw SqlFailure;
            if (Conflict || Config is not { IsActive: true } || Config.Id != snapshot.Id
                || Config.ConfigJson != snapshot.ConfigJson || Config.Status != snapshot.Status
                || Config.TablesJson != snapshot.TablesJson || Config.UpdatedAt != snapshot.UpdatedAt
                || !Connection.IsActive || Connection.CompanyId != snapshot.CompanyId) return Task.FromResult(false);
            Config.ConfigJson = dictionaryJson;
            Config.IsActive = isActive;
            Config.UpdatedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }
    }
}
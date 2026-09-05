using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Application.Interfaces;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Infrastructure.Services;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class PyCaretIntegrationTests
{
    private const string ApiKey = "test-only-internal-key";
    private const string DictionaryJson = "{\"label\":\"İstanbul\",\"tables\":[\"dbo.Orders\"]}";

    [Fact]
    public async Task SubmitSendsExactContractWithAuthenticationAndPersistsOriginalScope()
    {
        using var harness = new Harness();
        harness.Handler.Respond = async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/agent/analyze", request.RequestUri!.AbsolutePath);
            Assert.Equal(ApiKey, request.Headers.GetValues(PyCaretClient.ApiKeyHeader).Single());
            var body = (await request.Content!.ReadFromJsonAsync<JsonObject>())!;
            Assert.Equal(9, body.Count);
            Assert.Equal(harness.Query.Id.ToString(), body["query_id"]!.GetValue<string>());
            Assert.Equal(harness.Config.Id.ToString(), body["config_id"]!.GetValue<string>());
            Assert.Equal(harness.Config.CompanyId, body["company_id"]!.GetValue<string>());
            Assert.Equal(harness.Config.ConnectionId.ToString(), body["connection_id"]!.GetValue<string>());
            Assert.Equal(DictionaryJson, body["config_json"]!.GetValue<string>());
            Assert.Equal(InternalQueryScope.ComputeConfigHash(DictionaryJson), body["config_hash"]!.GetValue<string>());
            Assert.NotNull(PyCaretExecutionContext.Read(await harness.Db.QueryHistories.AsNoTracking().SingleAsync()));
            return harness.Response("queued");
        };

        await harness.Service.SubmitAsync(harness.Query, harness.Config, default);

        Assert.Equal("processing", harness.Query.Status);
        Assert.Null(harness.Query.CompletedAt);
        Assert.Equal("queued", JsonNode.Parse(harness.Query.ResultJson)!["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("status")]
    [InlineData("result")]
    [InlineData("cancel")]
    public async Task EveryReadAndCancelCarriesAllScopeParametersAndAuthentication(string operation)
    {
        using var harness = new Harness();
        harness.Handler.Respond = request =>
        {
            harness.AssertScopedRequest(request, operation);
            return Task.FromResult(harness.Response("processing"));
        };
        var scope = PyCaretExecutionContext.Read(harness.Query)!;
        _ = operation switch
        {
            "status" => await harness.Client.GetStatusAsync(scope, default),
            "result" => await harness.Client.GetResultAsync(scope, default),
            _ => await harness.Client.CancelAsync(scope, default)
        };
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Fact]
    public async Task PollingUsesOriginalHashAfterDictionaryEditAndPreservesResultAudit()
    {
        using var harness = new Harness();
        harness.Config.ConfigJson += " ";
        await harness.Db.SaveChangesAsync();
        harness.Handler.Respond = request =>
        {
            var operation = harness.Handler.Calls == 1 ? "status" : "result";
            harness.AssertScopedRequest(request, operation);
            var response = harness.Body("completed");
            response["audit"] = new JsonObject { ["truncated"] = true };
            response["charts"] = new JsonArray();
            response["newWorkerField"] = "preserved";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });
        };

        await harness.Service.RefreshAsync(harness.Query, default);

        Assert.Equal(2, harness.Handler.Calls);
        Assert.Equal("completed", harness.Query.Status);
        Assert.NotNull(harness.Query.CompletedAt);
        Assert.Equal(InternalQueryScope.ComputeConfigHash(DictionaryJson), PyCaretExecutionContext.Read(harness.Query)!.ConfigHash);
        Assert.Contains("newWorkerField", harness.Query.ResultJson);
        Assert.Contains("truncated", harness.Query.ResultJson);
    }

    [Theory]
    [InlineData("query_id")]
    [InlineData("company_id")]
    [InlineData("connection_id")]
    [InlineData("config_id")]
    [InlineData("config_hash")]
    public async Task WrongResponseScopeNeverPersistsWorkerData(string field)
    {
        using var harness = new Harness();
        harness.Handler.Respond = _ =>
        {
            var response = harness.Body("completed");
            response[field] = "wrong-scope";
            response["sensitiveResult"] = "must-not-persist";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });
        };
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("failed", harness.Query.Status);
        Assert.NotNull(harness.Query.CompletedAt);
        Assert.DoesNotContain("must-not-persist", harness.Query.ResultJson);
        Assert.Contains("scope", harness.Query.ResultJson);
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Fact]
    public async Task ClarificationPersistsStructuredProposalsAndOriginalAudit()
    {
        using var harness = new Harness();
        harness.Handler.Respond = _ =>
        {
            var response = harness.Body("needs_clarification");
            response["error"] = "Confirm this relationship?";
            response["pendingConfirmations"] = JsonNode.Parse("""
                [{"fromTable":"dbo.Orders","fromColumn":"CustomerId","toTable":"dbo.Customers",
                  "toColumn":"Id","fromColumns":["CustomerId"],"toColumns":["Id"],"valueOverlap":0.8}]
                """);
            response["audit"] = new JsonObject { ["decision"] = "pending" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });
        };
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("clarification", harness.Query.Status);
        Assert.NotNull(harness.Query.CompletedAt);
        Assert.Equal("Confirm this relationship?", harness.Query.ClarificationQuestion);
        var stored = JsonNode.Parse(harness.Query.ResultJson)!;
        var parameters = JsonNode.Parse(harness.Query.PyCaretParamsJson)!;
        Assert.True(JsonNode.DeepEquals(stored["pendingConfirmations"], parameters["relationship_proposals"]));
        Assert.Equal(0.8, parameters["relationship_proposals"]![0]!["valueOverlap"]!.GetValue<double>());
        Assert.NotNull(stored["audit"]);
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "not found")]
    [InlineData(HttpStatusCode.Unauthorized, "authentication")]
    [InlineData(HttpStatusCode.Forbidden, "authentication")]
    public async Task MissingWorkerOrAuthErrorTerminatesInsteadOfPollingForever(HttpStatusCode status, string message)
    {
        using var harness = new Harness();
        harness.Handler.Respond = _ => Task.FromResult(new HttpResponseMessage(status));
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("failed", harness.Query.Status);
        Assert.NotNull(harness.Query.CompletedAt);
        Assert.Contains(message, harness.Query.ResultJson);
    }

    [Fact]
    public async Task WorkerFailurePersistsErrorAndCompletionTime()
    {
        using var harness = new Harness();
        harness.Handler.Respond = _ => Task.FromResult(harness.Response("failed"));
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("failed", harness.Query.Status);
        Assert.NotNull(harness.Query.CompletedAt);
        Assert.Contains("error", harness.Query.ResultJson);
    }

    [Theory]
    [InlineData("submit", "unrecognized")]
    [InlineData("status", "unrecognized")]
    [InlineData("result", "unrecognized")]
    [InlineData("cancel", "unrecognized")]
    [InlineData("status", "")]
    [InlineData("status", null)]
    public async Task UnknownWorkerStatusFailsPermanentlyWithoutRetry(string operation, string? workerStatus)
    {
        using var harness = new Harness();
        var originalScope = PyCaretExecutionContext.Read(harness.Query)!;
        harness.Handler.Respond = _ =>
        {
            if (operation == "result" && harness.Handler.Calls == 1)
                return Task.FromResult(harness.Response("completed"));
            var body = harness.Body("unrecognized");
            if (workerStatus is null) body.Remove("status");
            else body["status"] = workerStatus;
            body["sensitiveResult"] = "must-not-persist";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        };

        if (operation == "submit") await harness.Service.SubmitAsync(harness.Query, harness.Config, default);
        else if (operation == "cancel") await harness.Service.CancelAsync(harness.Query, default);
        else await harness.Service.RefreshAsync(harness.Query, default);

        var stored = await harness.Db.QueryHistories.AsNoTracking().SingleAsync();
        Assert.Equal("failed", stored.Status);
        Assert.NotNull(stored.CompletedAt);
        Assert.Equal("PyCaret returned an unknown job status.", JsonNode.Parse(stored.ResultJson)!["error"]!.GetValue<string>());
        Assert.DoesNotContain("must-not-persist", stored.ResultJson);
        Assert.True(JsonNode.DeepEquals(originalScope.ToJson(), PyCaretExecutionContext.Read(stored)!.ToJson()));
        var expectedCalls = operation == "result" ? 2 : 1;
        Assert.Equal(expectedCalls, harness.Handler.Calls);

        harness.Db.ChangeTracker.Clear();
        var reloaded = await harness.Db.QueryHistories.SingleAsync();
        await harness.Service.RefreshAsync(reloaded, default);
        await harness.Service.CancelAsync(reloaded, default);

        Assert.Equal(expectedCalls, harness.Handler.Calls);
        Assert.Equal("failed", reloaded.Status);
        Assert.Equal(stored.CompletedAt, reloaded.CompletedAt);
        Assert.Equal(stored.ResultJson, reloaded.ResultJson);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task TransientHttpErrorsStayActiveOnlyUntilConfiguredDeadline(HttpStatusCode status)
    {
        using var harness = new Harness();
        harness.Settings["PyCaret:MaxJobDurationMinutes"] = "1";
        harness.Handler.Respond = _ => Task.FromResult(new HttpResponseMessage(status));
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("processing", harness.Query.Status);
        Assert.Null(harness.Query.CompletedAt);
        Assert.Contains("message", harness.Query.ResultJson);
        harness.Query.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        await harness.Db.SaveChangesAsync();
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("failed", harness.Query.Status);
        Assert.NotNull(harness.Query.CompletedAt);
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Fact]
    public async Task TransportFailureAndMalformedJsonRemainBounded()
    {
        using var harness = new Harness();
        harness.Handler.Respond = _ => throw new HttpRequestException("Do not expose sensitive transport detail");
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("processing", harness.Query.Status);
        Assert.DoesNotContain("sensitive", harness.Query.ResultJson);
        harness.Handler.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("not-json", System.Text.Encoding.UTF8, "application/json") });
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("processing", harness.Query.Status);
        harness.Query.CreatedAt = DateTime.UtcNow.AddMinutes(-16);
        await harness.Db.SaveChangesAsync();
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("failed", harness.Query.Status);
    }

    [Theory]
    [InlineData("cancelling")]
    [InlineData("cancelled")]
    public async Task CancellationIsPersistedBeforeSendingScopedCancel(string workerStatus)
    {
        using var harness = new Harness();
        harness.Handler.Respond = async request =>
        {
            harness.AssertScopedRequest(request, "cancel");
            var saved = await harness.Db.QueryHistories.AsNoTracking().SingleAsync();
            Assert.Equal("cancelling", saved.Status);
            var connection = new SavedConnection
            { Id = harness.Config.ConnectionId, CompanyId = harness.Config.CompanyId, IsActive = true };
            Assert.Throws<Grafirio.QueryPolicy.QueryPolicyException>(() => InternalQueryScope.Validate(
                harness.Config.CompanyId, saved.Id, saved.ConfigId, connection.Id,
                PyCaretExecutionContext.Read(saved)!.ConfigHash, saved, harness.Config, connection, ["dbo.Orders"]));
            return harness.Response(workerStatus);
        };
        await harness.Service.CancelAsync(harness.Query, default);
        Assert.Equal(workerStatus, harness.Query.Status);
        Assert.Equal(workerStatus == "cancelled", harness.Query.CompletedAt.HasValue);
    }

    [Fact]
    public async Task TransientCancellationIsRetriedWithoutRestoringProcessing()
    {
        using var harness = new Harness();
        harness.Handler.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await harness.Service.CancelAsync(harness.Query, default);
        Assert.Equal("cancelling", harness.Query.Status);
        harness.Handler.Respond = request =>
        {
            harness.AssertScopedRequest(request, "cancel");
            return Task.FromResult(harness.Response("processing"));
        };
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("cancelling", harness.Query.Status);
    }

    [Fact]
    public async Task MissingOriginalContextFailsWithoutRecomputingFromCurrentDictionary()
    {
        using var harness = new Harness();
        harness.Query.ResultJson = "{}";
        await harness.Db.SaveChangesAsync();
        await harness.Service.RefreshAsync(harness.Query, default);
        Assert.Equal("failed", harness.Query.Status);
        Assert.NotNull(harness.Query.CompletedAt);
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task MissingKeyStopsSubmitEndpointBeforeDatabaseOrLlmWork()
    {
        var identity = new Mock<IIdentityService>();
        identity.SetupGet(value => value.CurrentCompanyId).Returns(Guid.NewGuid());
        var service = new Mock<IPyCaretQueryService>(MockBehavior.Strict);
        service.SetupGet(value => value.IsConfigured).Returns(false);
        var method = typeof(AgentQueryEndpoints).GetMethod("SubmitQuery", BindingFlags.Static | BindingFlags.NonPublic)!;
        var task = (Task<IResult>)method.Invoke(null,
        [new AgentQueryEndpoints.QueryRequest(Guid.NewGuid(), "question"), null, null, service.Object,
            null, identity.Object, NullLogger<Grafirio.DataAnalysis.Api.Services.LlmAnalysisService>.Instance, CancellationToken.None])!;
        var result = await task;
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((IStatusCodeHttpResult)result).StatusCode);
        service.VerifyGet(value => value.IsConfigured, Times.Once);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmptyKeyDisablesHttpClientEvenWithAnAvailableWorker()
    {
        using var harness = new Harness();
        harness.Settings["Internal:ApiKey"] = " ";
        Assert.False(harness.Client.IsConfigured);
        var exception = await Assert.ThrowsAsync<PyCaretClientException>(() =>
            harness.Client.GetStatusAsync(PyCaretExecutionContext.Read(harness.Query)!, default));
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task CancelEndpointDoesNotDiscloseOrCancelAnotherCompanyQuery()
    {
        using var harness = new Harness();
        var identity = new Mock<IIdentityService>();
        identity.SetupGet(value => value.CurrentCompanyId).Returns(Guid.NewGuid());
        var service = new Mock<IPyCaretQueryService>(MockBehavior.Strict);
        var method = typeof(AgentQueryEndpoints).GetMethod("CancelQuery", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = await (Task<IResult>)method.Invoke(null,
            [harness.Query.Id, harness.Db, service.Object, identity.Object, CancellationToken.None])!;
        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } =
            _ => throw new InvalidOperationException("Unexpected HTTP request.");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Respond(request);
        }
    }

    private sealed class Harness : IDisposable
    {
        public DataAnalysisDbContext Db { get; } = new(new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public IConfigurationRoot Settings { get; } = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Internal:ApiKey"] = ApiKey, ["PyCaret:BaseUrl"] = "http://worker.test" }).Build();
        public Handler Handler { get; } = new();
        private readonly HttpClient _http;
        public PyCaretClient Client { get; }
        public PyCaretQueryService Service { get; }
        public AnalysisConfig Config { get; } = new()
        {
            Id = Guid.NewGuid(), CompanyId = Guid.NewGuid().ToString(), ConnectionId = Guid.NewGuid(),
            ConfigJson = DictionaryJson, TablesJson = "[\"dbo.Orders\"]", Status = "ready", IsActive = true
        };
        public QueryHistory Query { get; }

        public Harness()
        {
            _http = new HttpClient(Handler);
            Client = new(_http, Settings);
            Service = new(Db, Client, Settings, NullLogger<PyCaretQueryService>.Instance);
            Query = new QueryHistory
            {
                Id = Guid.NewGuid(), ConfigId = Config.Id, Status = "processing", CreatedAt = DateTime.UtcNow,
                Question = "question", PyCaretParamsJson = "{\"target_table\":\"dbo.Orders\"}"
            };
            Query.ResultJson = new JsonObject
            { [PyCaretExecutionContext.PropertyName] = PyCaretExecutionContext.Create(Query, Config).ToJson() }.ToJsonString();
            Db.AnalysisConfigs.Add(Config);
            Db.QueryHistories.Add(Query);
            Db.SaveChanges();
        }

        public JsonObject Body(string status) => new()
        {
            ["query_id"] = Query.Id.ToString(), ["company_id"] = Config.CompanyId,
            ["connection_id"] = Config.ConnectionId.ToString(), ["config_id"] = Config.Id.ToString(),
            ["config_hash"] = InternalQueryScope.ComputeConfigHash(DictionaryJson), ["status"] = status,
            ["message"] = "Worker message"
        };

        public HttpResponseMessage Response(string status) => new(HttpStatusCode.OK) { Content = JsonContent.Create(Body(status)) };

        public void AssertScopedRequest(HttpRequestMessage request, string operation)
        {
            Assert.Equal(operation == "cancel" ? HttpMethod.Post : HttpMethod.Get, request.Method);
            Assert.Equal(ApiKey, request.Headers.GetValues(PyCaretClient.ApiKeyHeader).Single());
            Assert.Equal($"/agent/analyze/{operation}/{Query.Id}", request.RequestUri!.AbsolutePath);
            var parameters = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
            Assert.Equal(4, parameters.Count);
            Assert.Equal(Config.CompanyId, parameters["company_id"].ToString());
            Assert.Equal(Config.Id.ToString(), parameters["config_id"].ToString());
            Assert.Equal(Config.ConnectionId.ToString(), parameters["connection_id"].ToString());
            Assert.Equal(InternalQueryScope.ComputeConfigHash(DictionaryJson), parameters["config_hash"].ToString());
        }

        public void Dispose() { _http.Dispose(); Db.Dispose(); ((IDisposable)Settings).Dispose(); }
    }
}
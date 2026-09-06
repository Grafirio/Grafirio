using System.Reflection;
using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Grafirio.Shared.Identity.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Driver;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.DataAnalysis.Tests;

public sealed class SavedConnectionPasswordTests
{
    private const string StoredCiphertext = "existing-encrypted-password";
    private const string SuppliedPassword = "test-only-new-password";
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    static SavedConnectionPasswordTests() =>
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", "test-only-connection-encryption-key");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task UpdatePreservesCiphertextWhenPasswordIsBlank(string? password)
    {
        await using var db = CreateDatabase();
        var connection = await SeedAsync(db);
        var logger = new CapturingLogger();
        var request = new UpdateConnectionRequest("renamed", null, null, null, null, password, null);

        var result = await InvokeAsync("UpdateConnection", connection.Id, request, Identity(), db,
            null!, null!, logger);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal("renamed", connection.Name);
        Assert.Equal(StoredCiphertext, connection.EncryptedPassword);
        AssertNoPassword(result, logger);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task SaveExistingPreservesCiphertextWhenPasswordIsBlank(string? password)
    {
        await using var db = CreateDatabase();
        var connection = await SeedAsync(db);
        var logger = new CapturingLogger();

        var result = await InvokeAsync("SaveConnection", SaveRequest(password), Identity(), db,
            null!, null!, logger);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(StoredCiphertext, connection.EncryptedPassword);
        Assert.Single(await db.SavedConnections.ToListAsync());
        AssertNoPassword(result, logger);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task CreateRejectsBlankPasswordWithoutWriting(string? password)
    {
        await using var db = CreateDatabase();
        var logger = new CapturingLogger();

        var result = await InvokeAsync("SaveConnection", SaveRequest(password), Identity(), db,
            null!, null!, logger);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Empty(await db.SavedConnections.ToListAsync());
        AssertNoPassword(result, logger);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("save-existing")]
    [InlineData("update")]
    public async Task SuppliedPasswordIsEncryptedAndNeverReturnedOrLogged(string operation)
    {
        await using var db = CreateDatabase();
        var connection = operation == "create" ? null : await SeedAsync(db);
        var logger = new CapturingLogger();

        // A failed bridge push must not undo a successful credential update.
        var result = operation == "update"
            ? await InvokeAsync("UpdateConnection", connection!.Id,
                new UpdateConnectionRequest(null, null, null, null, null, SuppliedPassword, null),
                Identity(), db, null!, null!, logger)
            : await InvokeAsync("SaveConnection", SaveRequest(SuppliedPassword), Identity(), db,
                null!, null!, logger);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var stored = Assert.Single(await db.SavedConnections.ToListAsync());
        Assert.StartsWith("v2:", stored.EncryptedPassword);
        Assert.NotEqual(StoredCiphertext, stored.EncryptedPassword);
        Assert.Equal(SuppliedPassword, EncryptionHelper.Decrypt(stored.EncryptedPassword));
        AssertNoPassword(result, logger);
    }

    [Fact]
    public async Task ReadResponsesDoNotExposeEitherPasswordRepresentation()
    {
        await using var db = CreateDatabase();
        var connection = await SeedAsync(db);
        var logger = new CapturingLogger();

        var list = await InvokeAsync("GetConnections", Identity(), db, logger);
        var single = await InvokeAsync("GetConnectionById", connection.Id, Identity(), db, logger);

        AssertNoPassword(list, logger);
        AssertNoPassword(single, logger);
    }

    [Fact]
    public async Task DecryptEndpointAndHandlerAreAbsent()
    {
        await using var app = WebApplication.CreateBuilder().Build();
        app.MapSavedConnectionEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();

        Assert.DoesNotContain(endpoints,
            endpoint => endpoint.RoutePattern.RawText!.Contains("decrypt", StringComparison.OrdinalIgnoreCase));
        Assert.Null(typeof(SavedConnectionEndpoints).GetMethod("GetDecryptedConnection",
            BindingFlags.NonPublic | BindingFlags.Static));
    }

    private static DataAnalysisDbContext CreateDatabase() => new(
        new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<SavedConnection> SeedAsync(DataAnalysisDbContext db)
    {
        var connection = new SavedConnection
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId.ToString(), UserId = UserId.ToString(),
            Name = "connection", Host = "database.test", Port = 1433, Database = "test",
            Username = "reader", EncryptedPassword = StoredCiphertext
        };
        db.SavedConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection;
    }

    private static IIdentityService Identity()
    {
        var identity = new Mock<IIdentityService>();
        identity.SetupGet(service => service.UserId).Returns(UserId);
        identity.SetupGet(service => service.CurrentCompanyId).Returns(CompanyId);
        return identity.Object;
    }

    private static SaveConnectionRequest SaveRequest(string? password) => new(
        UserId.ToString(), CompanyId.ToString(), "connection", "database.test", 1433,
        "test", "reader", password, true);

    private static Task<IResult> InvokeAsync(string method, params object[] arguments)
    {
        var bridges = new Mock<BridgeStore>(Mock.Of<IMongoDatabase>(), NullLogger<BridgeStore>.Instance);
        var db = arguments.OfType<DataAnalysisDbContext>().Single();
        var routes = db.SavedConnections.ToDictionary(connection => connection.Id, connection => new ConnectionRoute
        {
            Fingerprint = ConnectionRouteFingerprint.Create(connection), Revision = Guid.NewGuid().ToString("N")
        });
        bridges.Setup(store => store.GetConnectionRouteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, string _, CancellationToken _) => routes.GetValueOrDefault(id, new ConnectionRoute()));
        bridges.Setup(store => store.ReserveConnectionRouteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<ConnectionRoute>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, ConnectionRoute, string, CancellationToken>((id, _, route, revision, _) =>
                routes[id] = route with { Pending = true, Revision = revision }).Returns(Task.CompletedTask);
        bridges.Setup(store => store.CompleteConnectionRouteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<ConnectionRoute>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, ConnectionRoute, CancellationToken>((id, _, _, route, _) => routes[id] = route)
            .Returns(Task.CompletedTask);
        if (method is "SaveConnection" or "UpdateConnection")
        {
            var sync = new Mock<BridgeConnectionSync>(Mock.Of<IHubContext<BridgeHub>>(), bridges.Object,
                null!, Mock.Of<IServiceScopeFactory>(), NullLogger<BridgeConnectionSync>.Instance);
            sync.Setup(value => value.ForgetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<BridgeRegistry>(),
                It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            arguments[method == "UpdateConnection" ? 4 : 3] = sync.Object;
        }
        return (Task<IResult>)typeof(SavedConnectionEndpoints)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [.. arguments, bridges.Object])!;
    }

    private static void AssertNoPassword(IResult result, CapturingLogger logger)
    {
        var json = JsonSerializer.Serialize(((IValueHttpResult)result).Value);
        Assert.DoesNotContain(StoredCiphertext, json);
        Assert.DoesNotContain(SuppliedPassword, json);
        Assert.DoesNotContain("\"password\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"encryptedPassword\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StoredCiphertext, logger.Text);
        Assert.DoesNotContain(SuppliedPassword, logger.Text);
    }

    private sealed class CapturingLogger : ILogger<SaveConnectionRequest>
    {
        public string Text { get; private set; } = string.Empty;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Text += formatter(state, exception) + exception;
    }
}
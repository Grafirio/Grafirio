using Grafirio.Bridge.Desktop.LocalWorkspace.Data;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using Grafirio.Bridge.Desktop.LocalWorkspace.Services;
using Grafirio.Bridge.Desktop.LocalWorkspace.Storage;
using Xunit;

namespace Grafirio.Bridge.LocalWorkspace.Tests;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task BlankPasswordOnUpdateRetainsStoredSecret()
    {
        var store = new MemoryStore();
        var service = new LocalWorkspaceService(store, new RejectingDatabase());
        var snapshot = await service.SaveConnectionAsync(Connection(), CancellationToken.None);
        var id = snapshot.Connections.Single().Id;
        await service.SaveConnectionAsync(Connection() with { Id = id, Name = "Renamed", Password = "" }, CancellationToken.None);
        Assert.Equal("local-only-secret", store.Document.Connections.Single().Password);
        Assert.Equal("Renamed", store.Document.Connections.Single().Name);
    }

    [Fact]
    public async Task DraftAndSelectionSurviveServiceRecreation()
    {
        var store = new MemoryStore();
        var service = new LocalWorkspaceService(store, new RejectingDatabase());
        var snapshot = await service.SaveConnectionAsync(Connection(), CancellationToken.None);
        var selection = new WorkspaceSelection(snapshot.SelectedConnectionId, "SELECT 2 AS Result");
        await service.SaveSelectionAsync(selection, CancellationToken.None);
        var reloaded = await new LocalWorkspaceService(store, new RejectingDatabase()).ReadAsync(CancellationToken.None);
        Assert.Equal(selection.Draft, reloaded.Draft);
        Assert.Equal(selection.ConnectionId, reloaded.SelectedConnectionId);
    }

    [Fact]
    public async Task DeletionClearsSelectedConnectionButKeepsDraft()
    {
        var store = new MemoryStore();
        var service = new LocalWorkspaceService(store, new RejectingDatabase());
        var snapshot = await service.SaveConnectionAsync(Connection(), CancellationToken.None);
        var deleted = await service.DeleteConnectionAsync(snapshot.SelectedConnectionId!.Value, CancellationToken.None);
        Assert.Empty(deleted.Connections);
        Assert.Null(deleted.SelectedConnectionId);
        Assert.Equal(snapshot.Draft, deleted.Draft);
    }

    [Fact]
    public async Task UnknownConnectionCannotReachDatabase()
    {
        var service = new LocalWorkspaceService(new MemoryStore(), new RejectingDatabase());
        await Assert.ThrowsAsync<ArgumentException>(() => service.TestAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyNewPasswordIsRejected()
    {
        var service = new LocalWorkspaceService(new MemoryStore(), new RejectingDatabase());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveConnectionAsync(Connection() with { Password = "" }, CancellationToken.None));
    }

    private static LocalConnection Connection() => new()
    {
        Name = "Local", Host = "localhost", Database = "Sample", Username = "readonly",
        Password = "local-only-secret", AllowedTables = ["dbo.Sales"]
    };

    private sealed class MemoryStore : ILocalWorkspaceStore
    {
        public WorkspaceDocument Document { get; private set; } = new();
        public Task<WorkspaceDocument> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Document);
        public Task<WorkspaceDocument> UpdateAsync(Func<WorkspaceDocument, WorkspaceDocument> update, CancellationToken cancellationToken)
        {
            Document = update(Document);
            return Task.FromResult(Document);
        }
    }

    private sealed class RejectingDatabase : ILocalDatabase
    {
        public Task TestAsync(LocalConnection connection, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Database should not be contacted by this test.");
        public Task<LocalQueryResult> DiscoverAsync(LocalConnection connection, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Database should not be contacted by this test.");
        public Task<LocalQueryResult> QueryAsync(LocalConnection connection, LocalQueryRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Database should not be contacted by this test.");
    }
}
using System.Text.Json;
using Grafirio.Bridge.Desktop.LocalWorkspace.Export;
using Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using Grafirio.Bridge.Desktop.LocalWorkspace.Services;
using Xunit;

namespace Grafirio.Bridge.LocalWorkspace.Tests;

public sealed class WorkspaceDispatcherTests
{
    [Fact]
    public async Task DeactivationCancelsRunningQueryAndRejectsNewDatabaseWork()
    {
        var service = new BlockingService();
        using var dispatcher = new WorkspaceMessageDispatcher(service, new RejectingExporter());
        var query = dispatcher.DispatchAsync(Message("query", new LocalQueryRequest(Guid.NewGuid(), "SELECT 1", 1, 1)));
        Assert.False(query.IsCompleted);
        dispatcher.BeginDeactivation();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        foreach (var method in new[] { "query", "test", "discover", "export" })
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DispatchAsync(Message(method, new { })));
        dispatcher.EndDeactivation();
        await dispatcher.DispatchAsync(Message("load", new { }));
    }

    [Theory]
    [InlineData("saveSelection")]
    [InlineData("saveConnection")]
    public async Task DeactivationAndCancelDoNotCancelPersistence(string method)
    {
        var service = new BlockingService();
        using var dispatcher = new WorkspaceMessageDispatcher(service, new RejectingExporter());
        object payload = method == "saveSelection" ? new WorkspaceSelection(null, "latest") : new LocalConnection();
        var saving = dispatcher.DispatchAsync(Message(method, payload));
        dispatcher.BeginDeactivation();
        await dispatcher.DispatchAsync(Message("cancel", new { }));
        Assert.False(service.SaveToken.IsCancellationRequested);
        Assert.False(saving.IsCompleted);
        service.ReleaseSave.SetResult();
        await saving;
        Assert.False(service.SaveToken.IsCancellationRequested);
    }

    [Fact]
    public async Task DisposeIsIdempotentEvenWhileAQueryIsUnwinding()
    {
        var service = new BlockingService();
        var dispatcher = new WorkspaceMessageDispatcher(service, new RejectingExporter());
        var query = dispatcher.DispatchAsync(Message("query", new LocalQueryRequest(Guid.NewGuid(), "SELECT 1", 1, 1)));
        dispatcher.Dispose();
        dispatcher.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => dispatcher.DispatchAsync(Message("load", new { })));
    }

    private static WorkspaceMessage Message(string method, object payload) => WorkspaceProtocol.Parse(
        WorkspaceProtocol.DocumentUrl, JsonSerializer.Serialize(new { id = Guid.NewGuid(), method, payload }, WorkspaceProtocol.JsonOptions));

    private sealed class BlockingService : ILocalWorkspaceService
    {
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken SaveToken { get; private set; }
        public Task<WorkspaceSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot());
        public async Task<WorkspaceSnapshot> SaveConnectionAsync(LocalConnection connection, CancellationToken cancellationToken)
        {
            await SaveSelectionAsync(new(null, ""), cancellationToken);
            return Snapshot();
        }
        public async Task SaveSelectionAsync(WorkspaceSelection selection, CancellationToken cancellationToken)
        {
            SaveToken = cancellationToken;
            await ReleaseSave.Task.WaitAsync(cancellationToken);
        }
        public Task<WorkspaceSnapshot> DeleteConnectionAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TestAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LocalQueryResult> DiscoverAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<LocalQueryResult> QueryAsync(LocalQueryRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The query must be canceled.");
        }
        private static WorkspaceSnapshot Snapshot() => new([], null, "");
    }

    private sealed class RejectingExporter : ILocalCsvExporter
    {
        public Task<bool> ExportAsync(LocalQueryResult result, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
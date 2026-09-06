using Grafirio.Bridge.Desktop.LocalWorkspace.Export;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using Grafirio.Bridge.Desktop.LocalWorkspace.Services;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;

public sealed class WorkspaceMessageDispatcher(
    ILocalWorkspaceService service, ILocalCsvExporter exporter) : IDisposable
{
    private CancellationTokenSource? _active;
    private string? _activeMethod;
    private LocalQueryResult? _lastResult;
    private bool _disposed;
    private bool _deactivating;

    public async Task<object?> DispatchAsync(WorkspaceMessage message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (message.Method == "cancel") { Cancel(); return null; }
        if (_deactivating && message.Method is "query" or "test" or "discover" or "export")
            throw new OperationCanceledException("Local workspace is deactivating.");
        if (_active is not null) throw new InvalidOperationException("Başka bir işlem sürüyor.");
        using var cancellation = new CancellationTokenSource();
        _active = cancellation;
        _activeMethod = message.Method;
        try
        {
            var token = cancellation.Token;
            switch (message.Method)
            {
                case "load": return await service.ReadAsync(token);
                case "saveConnection":
                    _lastResult = null;
                    return await service.SaveConnectionAsync(WorkspaceProtocol.Payload<LocalConnection>(message), token);
                case "deleteConnection":
                    _lastResult = null;
                    return await service.DeleteConnectionAsync(WorkspaceProtocol.Payload<ConnectionReference>(message).Id, token);
                case "saveSelection":
                    await service.SaveSelectionAsync(WorkspaceProtocol.Payload<WorkspaceSelection>(message), token);
                    return null;
                case "test":
                    await service.TestAsync(WorkspaceProtocol.Payload<ConnectionReference>(message).Id, token);
                    return new { message = "Bağlantı başarılı. PostgreSQL/MySQL testi yalnızca bağlantıyı doğrular; serbest SQL kapalıdır." };
                case "discover":
                    return await service.DiscoverAsync(WorkspaceProtocol.Payload<ConnectionReference>(message).Id, token);
                case "query":
                    _lastResult = null;
                    _lastResult = await service.QueryAsync(WorkspaceProtocol.Payload<LocalQueryRequest>(message), token);
                    return _lastResult;
                case "export":
                    if (_lastResult is null) throw new ArgumentException("Önce bir sorgu çalıştırın.");
                    return new { exported = await exporter.ExportAsync(_lastResult, token) };
                default: throw new ArgumentException("Desteklenmeyen işlem.");
            }
        }
        finally { _active = null; _activeMethod = null; }
    }

    public void Cancel()
    {
        // A navigation request must not cancel a DPAPI write and lose pending work.
        if (_activeMethod is "query" or "test" or "discover") _active?.Cancel();
    }

    public void BeginDeactivation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _deactivating = true;
        Cancel();
    }

    public void EndDeactivation() => _deactivating = false;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active?.Cancel();
        _lastResult = null;
    }
}
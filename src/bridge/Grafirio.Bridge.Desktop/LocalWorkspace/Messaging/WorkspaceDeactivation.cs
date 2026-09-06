using System.Text.Json;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;

/// <summary>Correlates trusted lifecycle acknowledgements independently of the RPC method allowlist.</summary>
public sealed class WorkspaceDeactivation
{
    public static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(15);
    private TaskCompletionSource? _acknowledgement;
    private string? _requestId;

    public async Task FlushAsync(Action<object> send, TimeSpan timeout)
    {
        if (_acknowledgement is not null) throw new InvalidOperationException("A flush is already pending.");
        var acknowledgement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _acknowledgement = acknowledgement;
        _requestId = Guid.NewGuid().ToString("D");
        try
        {
            send(new { @event = "workspace-flush", id = _requestId });
            await acknowledgement.Task.WaitAsync(timeout);
        }
        finally
        {
            _acknowledgement = null;
            _requestId = null;
        }
    }

    public bool TryAcknowledge(string source, string json)
    {
        if (!WorkspaceProtocol.IsDocument(source) || json.Length > WorkspaceLimits.MaxMessageLength)
            throw new ArgumentException("Invalid lifecycle message source or size.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out var eventName)) return false;
        var properties = root.EnumerateObject().ToArray();
        if (eventName.ValueKind != JsonValueKind.String || eventName.GetString() != "workspace-flushed" ||
            properties.Select(property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length ||
            properties.Any(property => property.Name is not ("event" or "id" or "ok" or "error")) ||
            !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(id.GetString(), "D", out _) ||
            !root.TryGetProperty("ok", out var ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException("Invalid lifecycle acknowledgement.");
        var hasError = root.TryGetProperty("error", out var error);
        if ((ok.GetBoolean() && hasError) || (!ok.GetBoolean() &&
            (!hasError || error.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(error.GetString()))))
            throw new ArgumentException("Invalid lifecycle acknowledgement error.");
        if (id.GetString() != _requestId || _acknowledgement is null) return true;
        if (ok.GetBoolean()) _acknowledgement.TrySetResult();
        // Browser text is not included in logs or exceptions; it may contain user input.
        else _acknowledgement.TrySetException(new InvalidOperationException("Local workspace could not save pending work. Return to the local workspace and retry."));
        return true;
    }

    public void Abort() => _acknowledgement?.TrySetException(new ObjectDisposedException(nameof(LocalWorkspaceView)));
}
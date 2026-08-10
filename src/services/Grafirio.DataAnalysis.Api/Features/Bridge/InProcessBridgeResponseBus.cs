using System.Collections.Concurrent;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Tek replikali kurulumun cevap yolu: sahiplik bellekte, teslim dogrudan.
///
/// Varsayilan uygulama bu. Redis yapilandirilmamis bir kurulumda hicbir sey
/// degismiyor — tek replika calisiyorsa zaten her sorgunun sahibi ayni
/// surectir.
///
/// Sahiplik kaydi yine de tutuluyor: bilinmeyen bir istek kimligiyle gelen
/// cevabin sessizce dusmesi, cok replikali uygulamayla ayni davranis olmali ki
/// iki kurulum arasinda gecerken surpriz cikmasin.
/// </summary>
public sealed class InProcessBridgeResponseBus : IBridgeResponseBus
{
    private readonly ConcurrentDictionary<string, byte> _owned = new();
    private Func<BridgeResponse, CancellationToken, Task>? _handler;

    public string InstanceId { get; } = Environment.MachineName + ":" + Environment.ProcessId;

    public Task ClaimAsync(string requestId, CancellationToken ct = default)
    {
        _owned[requestId] = 0;
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string requestId, CancellationToken ct = default)
    {
        _owned.TryRemove(requestId, out _);
        return Task.CompletedTask;
    }

    public async Task DispatchAsync(BridgeResponse response, CancellationToken ct = default)
    {
        if (_owned.ContainsKey(response.RequestId) && _handler is { } handler)
            await handler(response, ct);
    }

    public void OnLocalDelivery(Func<BridgeResponse, CancellationToken, Task> handler) =>
        _handler = handler;
}

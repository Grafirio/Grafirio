using System.Text.Json;
using System.Reflection;
using Grafirio.Bridge.Contracts;

namespace Grafirio.Bridge;

/// <summary>Small durable, identity-bound outbox; telemetry failure never fails a customer query.</summary>
public sealed class BridgeTelemetryBuffer(string filePath)
{
    public const int Capacity = 1000;
    private readonly object gate = new();
    private readonly string instance = Guid.NewGuid().ToString("N");
    private readonly string version = typeof(BridgeTelemetryBuffer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    private Document document = new(null, [], 0);
    public int Dropped { get { lock (gate) return document.Dropped; } }
    public void SetOwner(Guid owner)
    {
        lock (gate)
        {
            try
            {
                if (File.Exists(filePath) && new FileInfo(filePath).Length <= 1024 * 1024)
                    document = JsonSerializer.Deserialize<Document>(File.ReadAllText(filePath)) ?? document;
                if (document.Owner != owner) document = new(owner, [], 0);
                document.Events = document.Events.TakeLast(Capacity).ToList();
                Save();
            }
            catch { document = new(owner, [], 1); }
        }
    }
    public void Record(string name, string? requestId = null)
    {
        if (!AllowedNames.Contains(name)) return;
        if (requestId is not null && !Guid.TryParse(requestId, out _)) requestId = null;
        lock (gate)
        {
            if (document.Owner is null) return;
            if (document.Events.Count == Capacity) { document.Events.RemoveAt(0); document.Dropped++; }
            document.Events.Add(new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, instance, version, name, requestId));
            try { Save(); } catch { document.Dropped++; }
        }
    }
    public BridgeTelemetryBatch Peek()
    {
        lock (gate)
        {
            document.Dropped += document.Events.RemoveAll(item => item.Timestamp < DateTime.UtcNow.AddDays(-7));
            return new(document.Events.Take(50).ToArray(), document.Dropped);
        }
    }
    public void Acknowledge(BridgeTelemetryBatch batch)
    {
        lock (gate)
        {
            var ids = batch.Events.Select(item => item.Id).ToHashSet();
            document.Events.RemoveAll(item => ids.Contains(item.Id));
            try { Save(); } catch { document.Dropped++; }
        }
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var temporary = filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document));
        File.Move(temporary, filePath, true);
    }
    private static readonly HashSet<string> AllowedNames = ["bridge.connected", "bridge.disconnected", "bridge.query.started", "bridge.query.completed", "bridge.query.failed"];
    public sealed record Document(Guid? Owner, List<BridgeTelemetryEvent> Events, int Dropped)
    {
        public List<BridgeTelemetryEvent> Events { get; set; } = Events;
        public int Dropped { get; set; } = Dropped;
    }
}

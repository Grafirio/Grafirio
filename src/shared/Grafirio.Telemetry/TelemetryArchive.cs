using System.Diagnostics;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Grafirio.Telemetry;

public sealed record ArchiveEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Service { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public string Environment { get; init; } = "";
    public string Version { get; init; } = "";
    public string Kind { get; init; } = "log";
    public string Name { get; init; } = "application.log";
    public string Level { get; init; } = "Information";
    public string? TraceId { get; init; } = Activity.Current?.TraceId.ToHexString();
    public string? SpanId { get; init; } = Activity.Current?.SpanId.ToHexString();
    public string? ParentSpanId { get; init; }
    public string? DiagnosticId { get; init; }
    public string? QueryId { get; init; }
    public string? RequestId { get; init; }
    public string? CompanyId { get; init; }
    public string? BridgeId { get; init; }
    public string? Category { get; init; }
    public string? ExceptionType { get; init; }
    public double? DurationMs { get; init; }
    public string? Status { get; init; }
    public ArchiveMetric? Metric { get; init; }
}

public sealed record ArchiveMetric(string Name, string? Unit, string Type, double? Value, long? Count, double? Sum, double? Min, double? Max, IReadOnlyDictionary<string, string>? Tags = null);

/// <summary>Bounded, optional export of the existing safe snapshot. An unavailable collector never blocks a query.</summary>
public sealed class TelemetryArchive : BackgroundService
{
    private readonly Channel<ArchiveEvent> queue = Channel.CreateBounded<ArchiveEvent>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.Wait });
    private readonly string service;
    private readonly string environment;
    private readonly Uri? endpoint;
    private readonly string? key;
    private readonly string instance;
    private long dropped;
    private long failures;
    private DateTime? lastSentAt;

    public TelemetryArchive(string service, string environment, IConfiguration configuration, string? instanceId = null)
    {
        this.service = service;
        this.environment = environment;
        instance = instanceId ?? Guid.NewGuid().ToString("N");
        key = configuration["TelemetryArchive:Key"];
        if (Uri.TryCreate(configuration["TelemetryArchive:Endpoint"], UriKind.Absolute, out var address)
            && (address.Scheme == "https" || (address.Scheme == "http" && configuration["TelemetryArchive:AllowInsecureTransport"] == "true"))
            && string.IsNullOrEmpty(address.UserInfo) && string.IsNullOrEmpty(address.Query) && string.IsNullOrEmpty(address.Fragment)
            && key?.Length >= 32 && key.All(character => character is >= (char)33 and <= (char)126))
            endpoint = address;
    }

    public bool Enabled => endpoint is not null;
    // Bridge acknowledges only after the collector's durable commit, on a separate background flush.
    public async Task<bool> ForwardBridgeAsync(IReadOnlyList<ArchiveEvent> events, long lost, CancellationToken ct)
    {
        if (!Enabled) return false;
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("X-Grafirio-Telemetry-Key", key);
            using var response = await client.PostAsJsonAsync(endpoint, new { events = events.Select(item => item with { Service = "bridge", Environment = environment }), dropped = lost }, ct);
            await RequireAcknowledgement(response, events.Count, ct);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { Interlocked.Increment(ref failures); return false; }
    }
    public object Status => new { enabled = Enabled, queued = queue.Reader.Count, dropped = Interlocked.Read(ref dropped), failures = Interlocked.Read(ref failures), lastSentAt };

    public void Record(ArchiveEvent item)
    {
        if (!Enabled) return;
        var record = item with { Service = item.Service == "bridge" ? "bridge" : service,
            Environment = environment, InstanceId = item.Service == "bridge" ? item.InstanceId : instance,
            Version = item.Service == "bridge" ? item.Version : GrafirioTelemetry.Version };
        if (!queue.Writer.TryWrite(record)) Interlocked.Increment(ref dropped);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled) return;
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Add("X-Grafirio-Telemetry-Key", key);
        var batch = new List<ArchiveEvent>(100);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (batch.Count == 0 && !await queue.Reader.WaitToReadAsync(stoppingToken)) break;
                while (batch.Count < 100 && queue.Reader.TryRead(out var item)) batch.Add(item);
                using var response = await client.PostAsJsonAsync(endpoint, new { events = batch, dropped = Interlocked.Read(ref dropped) }, stoppingToken);
                if ((int)response.StatusCode == 400 || (int)response.StatusCode == 413)
                { Interlocked.Add(ref dropped, batch.Count); batch.Clear(); }
                else { await RequireAcknowledgement(response, batch.Count, stoppingToken); batch.Clear(); lastSentAt = DateTime.UtcNow; }
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { Interlocked.Increment(ref failures); await Task.Delay(5000, stoppingToken); }
        }
    }
    private static async Task RequireAcknowledgement(HttpResponseMessage response, int expected, CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        var ack = await response.Content.ReadFromJsonAsync<ArchiveAcknowledgement>(ct);
        if (ack is null || ack.Accepted != expected || ack.Inserted < 0 || ack.Duplicates < 0 || ack.Inserted + ack.Duplicates != expected)
            throw new HttpRequestException("Invalid telemetry commit acknowledgement");
    }
    private sealed record ArchiveAcknowledgement(int Accepted, int Inserted, int Duplicates);
}

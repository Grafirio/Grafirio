using System.Text.Json;
using Grafirio.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Grafirio.Gateway.Tests;

public sealed class TelemetryArchiveTests
{
    [Fact]
    public void DisabledOrPlaintextExporterDoesNotQueueCustomerEvents()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["TelemetryArchive:Endpoint"] = "http://collector.invalid/api/telemetry/ingest", ["TelemetryArchive:Key"] = new('x', 40) }).Build();
        using var archive = new TelemetryArchive("gateway", "Testing", configuration);
        Assert.False(archive.Enabled);
        archive.Record(new ArchiveEvent());
        Assert.Equal(0, JsonSerializer.SerializeToElement(archive.Status).GetProperty("queued").GetInt32());
    }

    [Fact]
    public async Task CollectorFailureRetriesStableIdsAndOnlySendsSafeMetadata()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var received = new List<JsonElement>(); var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapPost("/ingest", async (HttpContext context) => {
            received.Add((await JsonDocument.ParseAsync(context.Request.Body)).RootElement.Clone());
            if (received.Count == 1) return Results.StatusCode(503);
            complete.TrySetResult(); return Results.Ok(new { accepted = 1, inserted = 1, duplicates = 0 });
        });
        await app.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["TelemetryArchive:Endpoint"] = app.Urls.Single() + "/ingest", ["TelemetryArchive:Key"] = new('x', 40),
            ["TelemetryArchive:AllowInsecureTransport"] = "true" }).Build();
        using var archive = new TelemetryArchive("gateway", "Testing", configuration);
        archive.Record(new ArchiveEvent { Category = "Safe.Category", TraceId = new('1', 32) });
        await archive.StartAsync(default);
        try {
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(received[0].GetProperty("events")[0].GetProperty("id").GetString(), received[1].GetProperty("events")[0].GetProperty("id").GetString());
            Assert.Equal("gateway", received[0].GetProperty("events")[0].GetProperty("service").GetString());
            Assert.DoesNotContain("message", received[0].ToString(), StringComparison.OrdinalIgnoreCase);
        } finally { await archive.StopAsync(default); await app.StopAsync(); }
    }

    [Fact]
    public async Task BridgeOutboxIsNotAcknowledgedByAnUnrelatedSuccessfulHttpResponse()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapPost("/ingest", () => Results.Ok(new { status = "ok" }));
        await app.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["TelemetryArchive:Endpoint"] = app.Urls.Single() + "/ingest", ["TelemetryArchive:Key"] = new('x', 40),
            ["TelemetryArchive:AllowInsecureTransport"] = "true" }).Build();
        using var archive = new TelemetryArchive("data-analysis", "Testing", configuration);
        try { Assert.False(await archive.ForwardBridgeAsync([new ArchiveEvent()], 0, default)); }
        finally { await app.StopAsync(); }
    }
}

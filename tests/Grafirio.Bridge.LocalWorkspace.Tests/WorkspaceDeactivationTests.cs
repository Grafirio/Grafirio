using System.Text.Json;
using Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;
using Xunit;

namespace Grafirio.Bridge.LocalWorkspace.Tests;

public sealed class WorkspaceDeactivationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task FlushWaitsForMatchingAcknowledgement()
    {
        var lifecycle = new WorkspaceDeactivation();
        string? id = null;
        var flush = lifecycle.FlushAsync(message => id = Id(message), TestTimeout);
        Assert.False(flush.IsCompleted);
        Assert.True(lifecycle.TryAcknowledge(WorkspaceProtocol.DocumentUrl, Ack(Guid.NewGuid().ToString("D"))));
        Assert.False(flush.IsCompleted);
        Assert.True(lifecycle.TryAcknowledge(WorkspaceProtocol.DocumentUrl, Ack(id!)));
        await flush;
    }

    [Fact]
    public async Task SaveFailureThrowsAndNextFlushCanRetry()
    {
        var lifecycle = new WorkspaceDeactivation();
        var failed = lifecycle.FlushAsync(message => lifecycle.TryAcknowledge(
            WorkspaceProtocol.DocumentUrl, Ack(Id(message), false)), TestTimeout);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        await lifecycle.FlushAsync(message => lifecycle.TryAcknowledge(
            WorkspaceProtocol.DocumentUrl, Ack(Id(message))), TestTimeout);
    }

    [Fact]
    public async Task MissingAcknowledgementTimesOutAndLateResponseCannotCompleteRetry()
    {
        var lifecycle = new WorkspaceDeactivation();
        string? expiredId = null;
        await Assert.ThrowsAsync<TimeoutException>(() => lifecycle.FlushAsync(
            message => expiredId = Id(message), TimeSpan.Zero));
        string? currentId = null;
        var retry = lifecycle.FlushAsync(message => currentId = Id(message), TestTimeout);
        lifecycle.TryAcknowledge(WorkspaceProtocol.DocumentUrl, Ack(expiredId!));
        Assert.False(retry.IsCompleted);
        lifecycle.TryAcknowledge(WorkspaceProtocol.DocumentUrl, Ack(currentId!));
        await retry;
    }

    [Theory]
    [InlineData("https://attacker.invalid/index.html")]
    [InlineData(WorkspaceProtocol.DocumentUrl + "?query=1")]
    public void UntrustedAcknowledgementIsRejected(string source)
    {
        Assert.Throws<ArgumentException>(() => new WorkspaceDeactivation().TryAcknowledge(source, Ack(Guid.NewGuid().ToString("D"))));
    }

    [Theory]
    [InlineData("{\"event\":4}")]
    [InlineData("{\"event\":\"workspace-flushed\"}")]
    [InlineData("{\"event\":\"workspace-flushed\",\"id\":\"invalid\",\"ok\":true}")]
    public void MalformedAcknowledgementIsRejected(string json)
    {
        Assert.Throws<ArgumentException>(() => new WorkspaceDeactivation().TryAcknowledge(WorkspaceProtocol.DocumentUrl, json));
    }

    [Fact]
    public void AcknowledgementIsNotAnRpcMethod()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceProtocol.Parse(WorkspaceProtocol.DocumentUrl,
            JsonSerializer.Serialize(new { id = Guid.NewGuid(), method = "workspace-flushed", payload = new { } })));
        Assert.Throws<JsonException>(() => WorkspaceProtocol.Parse(WorkspaceProtocol.DocumentUrl, Ack(Guid.NewGuid().ToString("D"))));
    }

    [Fact]
    public async Task AbortFailsPendingFlushAndIsIdempotent()
    {
        var lifecycle = new WorkspaceDeactivation();
        var pending = lifecycle.FlushAsync(_ => { }, TestTimeout);
        lifecycle.Abort();
        lifecycle.Abort();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
    }

    [Fact]
    public void DuplicateAndUnexpectedAcknowledgementFieldsAreRejected()
    {
        var lifecycle = new WorkspaceDeactivation();
        var acknowledgement = Ack(Guid.NewGuid().ToString("D"));
        Assert.Throws<ArgumentException>(() => lifecycle.TryAcknowledge(WorkspaceProtocol.DocumentUrl,
            acknowledgement.Replace("\"ok\":true", "\"ok\":true,\"Ok\":true")));
        Assert.Throws<ArgumentException>(() => lifecycle.TryAcknowledge(WorkspaceProtocol.DocumentUrl,
            acknowledgement.Replace("\"ok\":true", "\"ok\":true,\"payload\":{}")));
    }

    [Fact]
    public async Task TransportFailureIsPropagatedAndDoesNotBlockRetry()
    {
        var lifecycle = new WorkspaceDeactivation();
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.FlushAsync(
            _ => throw new InvalidOperationException("Transport failed."), TestTimeout));
        await lifecycle.FlushAsync(message => lifecycle.TryAcknowledge(
            WorkspaceProtocol.DocumentUrl, Ack(Id(message))), TestTimeout);
    }

    private static string Id(object message) => JsonSerializer.SerializeToElement(message).GetProperty("id").GetString()!;

    private static string Ack(string id, bool ok = true) => ok
        ? JsonSerializer.Serialize(new { @event = "workspace-flushed", id, ok })
        : JsonSerializer.Serialize(new { @event = "workspace-flushed", id, ok, error = "Save failed." });
}
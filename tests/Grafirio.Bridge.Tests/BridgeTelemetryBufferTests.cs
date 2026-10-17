using Grafirio.Bridge;

namespace Grafirio.Bridge.Tests;

public sealed class BridgeTelemetryBufferTests
{
    private static string PathForTest() => Path.Combine(Path.GetTempPath(), "grafirio-bridge-telemetry-" + Guid.NewGuid(), "outbox.json");

    [Fact]
    public void OutboxSurvivesRestartAndKeepsIdsUntilAcknowledged()
    {
        var path = PathForTest(); var owner = Guid.NewGuid();
        var first = new BridgeTelemetryBuffer(path); first.SetOwner(owner);
        first.Record("bridge.query.completed", Guid.NewGuid().ToString("N"));
        var batch = first.Peek();
        var second = new BridgeTelemetryBuffer(path); second.SetOwner(owner);
        Assert.Equal(batch.Events[0].Id, Assert.Single(second.Peek().Events).Id);
        second.Acknowledge(batch);
        var third = new BridgeTelemetryBuffer(path); third.SetOwner(owner);
        Assert.Empty(third.Peek().Events);
    }

    [Fact]
    public void RequestIdCannotCarryRawSqlOrCredentialsToDisk()
    {
        var path = PathForTest(); var outbox = new BridgeTelemetryBuffer(path); outbox.SetOwner(Guid.NewGuid());
        outbox.Record("bridge.query.failed", "SELECT password=customer-secret");
        Assert.Null(Assert.Single(outbox.Peek().Events).RequestId);
        Assert.DoesNotContain("customer-secret", File.ReadAllText(path));
    }

    [Fact]
    public void ReEnrollmentCannotUploadPreviousOwnersRecords()
    {
        var outbox = new BridgeTelemetryBuffer(PathForTest()); outbox.SetOwner(Guid.NewGuid());
        outbox.Record("bridge.connected");
        outbox.SetOwner(Guid.NewGuid());
        Assert.Empty(outbox.Peek().Events);
    }

    [Fact]
    public void FullOutboxRemainsBoundedAndCountsDiscardedEvents()
    {
        // Invalid directory intentionally exercises disk failures too, without touching customer state.
        var directory = Path.GetDirectoryName(PathForTest())!; Directory.CreateDirectory(directory);
        var outbox = new BridgeTelemetryBuffer(directory); outbox.SetOwner(Guid.NewGuid());
        for (var i = 0; i < BridgeTelemetryBuffer.Capacity + 2; i++) outbox.Record("bridge.connected");
        Assert.True(outbox.Dropped >= 2);
        Assert.Equal(50, outbox.Peek().Events.Count);
        outbox.Record("SELECT customer-secret");
        Assert.DoesNotContain(outbox.Peek().Events, item => item.Name.Contains("secret"));
    }
}

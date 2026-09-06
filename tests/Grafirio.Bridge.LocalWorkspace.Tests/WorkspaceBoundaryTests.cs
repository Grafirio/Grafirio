using System.Text.Json;
using Grafirio.Bridge.Desktop.LocalWorkspace;
using Grafirio.Bridge.Desktop.LocalWorkspace.Export;
using Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;
using Grafirio.Bridge.Desktop.LocalWorkspace.Models;
using Xunit;

namespace Grafirio.Bridge.LocalWorkspace.Tests;

public sealed class WorkspaceBoundaryTests
{
    [Theory]
    [InlineData("https://local-workspace.grafirio.invalid/index.html?x=1")]
    [InlineData("https://local-workspace.grafirio.invalid/index.html#fragment")]
    [InlineData("https://local-workspace.grafirio.invalid/other.html")]
    [InlineData("https://local-workspace.grafirio.invalid.attacker.test/index.html")]
    [InlineData("file:///index.html")]
    [InlineData("http://local-workspace.grafirio.invalid/index.html")]
    public void UntrustedOriginsAndPathsAreRejected(string source)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceProtocol.Parse(source, Message("load")));
    }

    [Fact]
    public void ExactPackagedOriginIsAccepted()
    {
        Assert.Equal("load", WorkspaceProtocol.Parse(WorkspaceProtocol.DocumentUrl, Message("load")).Method);
    }

    [Theory]
    [InlineData("navigate")]
    [InlineData("executeScript")]
    [InlineData("readFile")]
    [InlineData("getPassword")]
    public void UnknownMethodsAreRejected(string method)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceProtocol.Parse(WorkspaceProtocol.DocumentUrl, Message(method)));
    }

    [Fact]
    public void DuplicateFieldsAreRejected()
    {
        var json = Message("load").Replace("\"method\":\"load\"", "\"method\":\"load\",\"Method\":\"query\"");
        Assert.Throws<ArgumentException>(() => WorkspaceProtocol.Parse(WorkspaceProtocol.DocumentUrl, json));
    }

    [Fact]
    public void UnexpectedPayloadFieldsAreRejected()
    {
        var json = Message("export").Replace("\"payload\":{}", "\"payload\":{\"path\":\"C:/file.csv\"}");
        Assert.Throws<ArgumentException>(() => WorkspaceProtocol.Parse(WorkspaceProtocol.DocumentUrl, json));
    }

    [Fact]
    public void ArbitraryAssetsAreNeverServed()
    {
        Assert.Null(WorkspaceAssets.Find(WorkspaceProtocol.Origin + "/../local-workspace.dpapi"));
        Assert.Null(WorkspaceAssets.Find(WorkspaceProtocol.Origin + "/app.js?x=1"));
        Assert.NotNull(WorkspaceAssets.Find(WorkspaceProtocol.DocumentUrl));
    }

    [Theory]
    [InlineData("=1+2", "\"'=1+2\"")]
    [InlineData("  @SUM(A1)", "\"'  @SUM(A1)\"")]
    [InlineData("-10", "\"'-10\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData(null, "\"\"")]
    public void CsvEscapesCellsAndNeutralizesFormulas(string? value, string expected)
    {
        Assert.Equal(expected, CsvEncoding.Cell(value));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(5001, 30)]
    [InlineData(100, 0)]
    [InlineData(100, 121)]
    public void QueryLimitsAreEnforced(int rows, int timeout)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceValidation.Query(new(Guid.NewGuid(), "SELECT 1", rows, timeout)));
    }

    [Fact]
    public void SummaryNeverSerializesPassword()
    {
        var connection = new LocalConnection { Password = "sensitive-sample-value" };
        var json = JsonSerializer.Serialize(connection.ToSummary(), WorkspaceProtocol.JsonOptions);
        Assert.DoesNotContain("sensitive-sample-value", json);
        Assert.DoesNotContain("\"password\":", json);
        Assert.Contains("\"hasPassword\":true", json);
    }

    private static string Message(string method) => JsonSerializer.Serialize(
        new { id = Guid.NewGuid().ToString("D"), method, payload = new { } });
}
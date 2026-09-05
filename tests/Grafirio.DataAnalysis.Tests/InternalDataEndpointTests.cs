using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data.Access;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Grafirio.DataAnalysis.Tests;

public sealed class InternalDataEndpointTests
{
    private const int QueryTimeoutSeconds = 300;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    public async Task InvalidAuthenticationCannotReadSelectionOrOpenDataSource(string? apiKey)
    {
        await using var harness = new InternalDataEndpointHarness();
        var response = await harness.SendAsync(apiKey: apiKey);
        Assert.Equal(StatusCodes.Status401Unauthorized, response.Response.StatusCode);
        harness.Collection.VerifyNoOtherCalls();
        harness.DataSources.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExplicitlyDisabledKeyFailsClosedWithoutEnvironmentFallback()
    {
        await using var harness = new InternalDataEndpointHarness();
        harness.Configuration["Internal:ApiKey"] = " ";
        var response = await harness.SendAsync();
        Assert.Equal(StatusCodes.Status401Unauthorized, response.Response.StatusCode);
        harness.Collection.VerifyNoOtherCalls();
        harness.DataSources.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"connectionId\":\"not-a-guid\"}")]
    public async Task InvalidJsonContractIsRejectedByRealEndpointBinding(string json)
    {
        await using var harness = new InternalDataEndpointHarness();
        var response = await harness.SendJsonAsync(json);
        Assert.Equal(StatusCodes.Status400BadRequest, response.Response.StatusCode);
        harness.Collection.VerifyNoOtherCalls();
        harness.DataSources.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptySqlIsRejectedBeforeSelectionRead(string sql)
    {
        await using var harness = new InternalDataEndpointHarness();
        var response = await harness.SendAsync(harness.Request with { Sql = sql });
        Assert.Equal(StatusCodes.Status400BadRequest, response.Response.StatusCode);
        harness.Collection.VerifyNoOtherCalls();
        harness.DataSources.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("company")]
    [InlineData("query")]
    [InlineData("config")]
    [InlineData("connection")]
    [InlineData("hash")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("inactive-config")]
    [InlineData("inactive-connection")]
    [InlineData("selection")]
    public async Task InvalidPersistedScopeNeverOpensDataSource(string change)
    {
        await using var harness = new InternalDataEndpointHarness();
        var request = harness.Request;
        switch (change)
        {
            case "company": request = request with { CompanyId = "company-b" }; break;
            case "query": request = request with { QueryId = Guid.NewGuid() }; break;
            case "config": request = request with { ConfigId = Guid.NewGuid() }; break;
            case "connection": request = request with { ConnectionId = Guid.NewGuid() }; break;
            case "hash": request = request with { ConfigHash = new string('0', 64) }; break;
            case "completed": harness.Query.Status = "completed"; break;
            case "cancelled": harness.Query.Status = "cancelled"; break;
            case "inactive-config": harness.Config.IsActive = false; break;
            case "inactive-connection": harness.Connection.IsActive = false; break;
            case "selection": harness.SelectedTables = ["dbo.Other"]; break;
        }

        var response = await harness.SendAsync(request);
        Assert.Equal(StatusCodes.Status403Forbidden, response.Response.StatusCode);
        using var body = await JsonDocument.ParseAsync(response.Response.Body);
        Assert.Equal("Query execution scope is invalid or stale.", body.RootElement.GetProperty("error").GetString());
        harness.DataSources.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("SELECT Id FROM Orders")]
    [InlineData("SELECT * FROM dbo.Other")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.COLUMNS")]
    [InlineData("SELECT * FROM sys.tables")]
    [InlineData("SELECT Id FROM dbo.Orders WHERE EXISTS (SELECT 1 FROM dbo.Other)")]
    [InlineData("SELECT * INTO dbo.Copy FROM dbo.Orders")]
    [InlineData("SELECT NEXT VALUE FOR dbo.Sequence")]
    public async Task ValidScopeCannotBypassStrictSqlPolicy(string sql)
    {
        await using var harness = new InternalDataEndpointHarness();
        var response = await harness.SendAsync(harness.Request with { Sql = sql });
        Assert.Equal(StatusCodes.Status400BadRequest, response.Response.StatusCode);
        using var body = await JsonDocument.ParseAsync(response.Response.Body);
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetString()));
        harness.DataSources.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, 50001)]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(2, 3)]
    [InlineData(100001, 100001)]
    public async Task BoundRowLimitIsClampedAndOneExtraRowIsRequested(int? maxRows, int expectedReadLimit)
    {
        await using var harness = new InternalDataEndpointHarness();
        harness.AllowSession();
        harness.Session.Setup(value => value.StreamAsync(InternalDataEndpointHarness.AllowedSql, null,
                QueryTimeoutSeconds, expectedReadLimit, It.IsAny<CancellationToken>()))
            .Returns(Rows(0));
        var response = await harness.SendAsync(harness.Request with { MaxRows = maxRows });
        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        using var body = await JsonDocument.ParseAsync(response.Response.Body);
        Assert.Equal(0, body.RootElement.GetProperty("rowCount").GetInt32());
        Assert.Empty(body.RootElement.GetProperty("columns").EnumerateArray());
        Assert.False(body.RootElement.GetProperty("truncated").GetBoolean());
        harness.Session.VerifyAll();
        harness.DataSources.VerifyAll();
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task BoundParametersAndTruncationArePreservedInSerializedResponse(int rowCount, bool truncated)
    {
        const int rowLimit = 2;
        await using var harness = new InternalDataEndpointHarness();
        harness.AllowSession();
        harness.Session.Setup(value => value.StreamAsync(InternalDataEndpointHarness.AllowedSql,
                It.IsAny<object>(), QueryTimeoutSeconds, rowLimit + 1, It.IsAny<CancellationToken>()))
            .Callback<string, object?, int?, int?, CancellationToken>((_, parameters, _, _, _) =>
            {
                var values = Assert.IsType<Dictionary<string, object?>>(parameters);
                Assert.Equal(4, values.Count);
                Assert.Equal(7L, Convert.ToInt64(values["minimum"]));
                Assert.Equal("İstanbul", values["city"]);
                Assert.Equal(true, values["active"]);
                Assert.Null(values["missing"]);
                Assert.DoesNotContain(values.Values, value => value is JsonElement);
            })
            .Returns(Rows(rowCount));
        var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            "{\"minimum\":7,\"city\":\"İstanbul\",\"active\":true,\"missing\":null}");

        var response = await harness.SendAsync(harness.Request with { Parameters = parameters, MaxRows = rowLimit });
        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        using var body = await JsonDocument.ParseAsync(response.Response.Body);
        Assert.Equal(rowLimit, body.RootElement.GetProperty("rowCount").GetInt32());
        Assert.Equal(truncated, body.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("Id", Assert.Single(body.RootElement.GetProperty("columns").EnumerateArray()).GetString());
        Assert.Equal(new[] { 1, 2 }, body.RootElement.GetProperty("rows").EnumerateArray()
            .Select(row => row.GetProperty("Id").GetInt32()));
        harness.Session.VerifyAll();
        harness.DataSources.VerifyAll();
    }

    private static async IAsyncEnumerable<QueryRow> Rows(int count)
    {
        for (var index = 1; index <= count; index++)
            yield return new QueryRow(new Dictionary<string, object?> { ["Id"] = index });
        await Task.CompletedTask;
    }
}
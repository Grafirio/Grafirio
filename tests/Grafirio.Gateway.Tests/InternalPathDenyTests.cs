using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Grafirio.Gateway.Tests;

public sealed class InternalPathDenyTests
{
    [Theory]
    [InlineData("/data-analysis/internal")]
    [InlineData("/data-analysis/internal/")]
    [InlineData("/data-analysis/internal/query")]
    [InlineData("/DATA-ANALYSIS/INTERNAL/query")]
    public async Task InternalPathsNeverReachProxyRegardlessOfKey(string path)
    {
        foreach (var key in new[] { string.Empty, "test-only-internal-key" })
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Request.Headers["X-Internal-Key"] = key;
            context.Request.Headers.Authorization = "Bearer test-token";
            var forwarded = false;

            await global::Program.RejectInternalRequestsAsync(context, _ =>
            {
                forwarded = true;
                return Task.CompletedTask;
            });

            Assert.False(forwarded);
            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/data-analysis/internalized")]
    [InlineData("/data-analysis/api/connections")]
    [InlineData("/health")]
    public async Task NonInternalPathsContinueToProxy(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var forwarded = false;

        await global::Program.RejectInternalRequestsAsync(context, _ =>
        {
            forwarded = true;
            return Task.CompletedTask;
        });

        Assert.True(forwarded);
    }

    [Theory]
    [InlineData("/data-analysis/%69nternal/query")]
    [InlineData("/data-analysis/public/../internal/query")]
    [InlineData("/data-analysis/public/%2e%2e/internal/query")]
    [InlineData("/data-analysis/./internal/query")]
    [InlineData("/data-analysis//internal/query")]
    [InlineData("//data-analysis/internal/query")]
    [InlineData("/data-analysis%2finternal/query")]
    [InlineData("/data-analysis/%2569nternal/query")]
    [InlineData("/data-analysis/%252e%252e/data-analysis/internal/query")]
    [InlineData("/data-analysis%5cinternal/query")]
    [InlineData("/data-analysis\\internal/query")]
    public async Task RawRequestNormalizationCannotBypassDeny(string rawPath)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var forwarded = false;
        app.Use(global::Program.RejectInternalRequestsAsync);
        app.Run(context =>
        {
            forwarded = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        await app.StartAsync();

        try
        {
            var address = new Uri(Assert.Single(app.Urls));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = new TcpClient();
            await client.ConnectAsync(address.Host, address.Port, timeout.Token);
            await using var stream = client.GetStream();
            // HttpClient would normalize dot segments before Kestrel sees the attack.
            var request = $"GET {rawPath} HTTP/1.1\r\nHost: localhost\r\nX-Internal-Key: test-only-internal-key\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
            using var reader = new StreamReader(stream);
            var status = await reader.ReadLineAsync(timeout.Token);

            Assert.NotNull(status);
            Assert.True(status.StartsWith("HTTP/1.1 400", StringComparison.Ordinal)
                || status.StartsWith("HTTP/1.1 404", StringComparison.Ordinal), status);
            Assert.False(forwarded);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
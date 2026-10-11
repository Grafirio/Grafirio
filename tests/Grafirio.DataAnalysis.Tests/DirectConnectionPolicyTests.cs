using System.Net;
using Grafirio.DataAnalysis.Api.Data.Access;
using Microsoft.Extensions.Configuration;

namespace Grafirio.DataAnalysis.Tests;

public sealed class DirectConnectionPolicyTests
{
    private static DataSourceTarget Target(string host) => new(host, 1433, "sample", "reader", "secret", false);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("168.63.129.16")]
    [InlineData("10.0.0.2")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("fc00::1")]
    public async Task DirectConnectionsCannotProbeInfrastructureOrUnapprovedPrivateNetworks(string host)
    {
        var policy = new DirectConnectionPolicy(new ConfigurationBuilder().Build());
        var exception = await Assert.ThrowsAsync<DataSourceException>(() => policy.AuthorizeAsync(Target(host), default));
        Assert.StartsWith("DIRECT_TARGET_BLOCKED", exception.Message);
    }

    [Fact]
    public async Task PublicHostnameIsResolvedOncePinnedAndRetainsCertificateName()
    {
        var calls = 0;
        var policy = new DirectConnectionPolicy(new ConfigurationBuilder().Build(), (_, _) =>
        {
            calls++;
            return Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
        });
        var approved = await policy.AuthorizeAsync(Target("sql.example"), default);
        Assert.Equal("8.8.8.8", approved.Host);
        Assert.Equal("sql.example", approved.CertificateHost);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AnyBlockedDnsAnswerRejectsWholeTarget()
    {
        var policy = new DirectConnectionPolicy(new ConfigurationBuilder().Build(), (_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Loopback }));
        await Assert.ThrowsAsync<DataSourceException>(() => policy.AuthorizeAsync(Target("sql.example"), default));
    }

    [Fact]
    public async Task ExplicitPrivateHostAllowlistDoesNotAllowMetadata()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DirectConnections:AllowedPrivateHosts:0"] = "sql.corporate" }).Build();
        var policy = new DirectConnectionPolicy(configuration, (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.1.2.3") }));
        Assert.Equal("10.1.2.3", (await policy.AuthorizeAsync(Target("sql.corporate"), default)).Host);
        await Assert.ThrowsAsync<DataSourceException>(() => policy.AuthorizeAsync(Target("169.254.169.254"), default));
    }
}

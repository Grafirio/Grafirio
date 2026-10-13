using System.Text.Json;

namespace Grafirio.Gateway.Tests;

public sealed class OpsRoutesTests
{
    private static JsonElement ReverseProxy()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        return config.RootElement.GetProperty("ReverseProxy").Clone();
    }

    [Fact]
    public void Operator_route_requires_platform_admin_and_targets_the_ops_cluster()
    {
        var reverseProxy = ReverseProxy();
        var route = reverseProxy.GetProperty("Routes").GetProperty("ops-route");

        Assert.Equal("PlatformAdmin", route.GetProperty("AuthorizationPolicy").GetString());
        Assert.Equal("ops.cluster", route.GetProperty("ClusterId").GetString());
        Assert.Equal("/api/ops/{**catch-all}", route.GetProperty("Transforms")[0].GetProperty("PathPattern").GetString());
        Assert.True(reverseProxy.GetProperty("Clusters").TryGetProperty("ops.cluster", out _));
    }

    [Fact]
    public void Only_the_detail_free_summary_is_anonymous()
    {
        var routes = ReverseProxy().GetProperty("Routes");
        var summary = routes.GetProperty("ops-summary-route");

        Assert.Equal("Anonymous", summary.GetProperty("AuthorizationPolicy").GetString());
        Assert.Equal("/ops/status/summary", summary.GetProperty("Match").GetProperty("Path").GetString());
        Assert.Equal("/api/ops/status/summary", summary.GetProperty("Transforms")[0].GetProperty("PathSet").GetString());

        var anonymousOpsRoutes = routes.EnumerateObject()
            .Where(route => route.Value.GetProperty("ClusterId").GetString() == "ops.cluster"
                && route.Value.GetProperty("AuthorizationPolicy").GetString() == "Anonymous")
            .Select(route => route.Name);
        Assert.Equal(["ops-summary-route"], anonymousOpsRoutes);
    }
}

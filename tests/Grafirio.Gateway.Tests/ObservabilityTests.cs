using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Grafirio.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Grafirio.Gateway.Tests;

public sealed class ObservabilityTests
{
    [Fact]
    public void Gateway_routes_are_exact_admin_only_and_use_existing_clusters()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        var routes = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        foreach (var service in new[] { "identity", "commerce", "data-analysis" })
        {
            var route = routes.GetProperty("observability-" + service);
            Assert.Equal("PlatformAdmin", route.GetProperty("AuthorizationPolicy").GetString());
            Assert.Equal(service + ".cluster", route.GetProperty("ClusterId").GetString());
            Assert.Equal("/admin/observability/" + service, route.GetProperty("Match").GetProperty("Path").GetString());
            Assert.Equal("/api/admin/observability", route.GetProperty("Transforms")[0].GetProperty("PathSet").GetString());
        }
    }

    private static JsonElement Read(ObservabilitySnapshot snapshot, int minutes = 15, int limit = 200,
        string? level = null, string? traceId = null) => JsonSerializer.SerializeToElement(
            snapshot.Read(minutes, limit, level, traceId), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void Buffers_are_bounded_and_filters_run_before_the_result_limit()
    {
        var snapshot = new ObservabilitySnapshot("test", "Testing");
        for (var index = 0; index < 1010; index++)
        {
            snapshot.AddLog(new LogSample(DateTime.UtcNow, index % 2 == 0 ? "Error" : "Information",
                "test", index.ToString(), null, "trace", null));
            snapshot.AddTrace(new TraceSample(DateTime.UtcNow, "test", "Internal", "Ok", 1,
                "trace", index.ToString(), null, []));
        }
        var json = Read(snapshot, limit: 9999, level: "Error", traceId: "trace");
        Assert.Equal(10, json.GetProperty("droppedLogs").GetInt32());
        Assert.Equal(10, json.GetProperty("droppedTraces").GetInt32());
        Assert.Equal(500, json.GetProperty("matchingLogs").GetInt32());
        Assert.Equal(200, json.GetProperty("logs").GetArrayLength());
        Assert.Equal("1008", json.GetProperty("logs")[0].GetProperty("message").GetString());
        Assert.Equal(200, json.GetProperty("traces").GetArrayLength());
        Assert.Equal(0, Read(snapshot, traceId: "missing").GetProperty("matchingLogs").GetInt32());
    }

    [Fact]
    public void Expired_events_are_removed_and_time_windows_do_not_filter_metrics()
    {
        var snapshot = new ObservabilitySnapshot("test", "Testing");
        snapshot.AddLog(new LogSample(DateTime.UtcNow.AddHours(-1), "Error", "test", "expired", null, null, null));
        snapshot.AddTrace(new TraceSample(DateTime.UtcNow.AddHours(-1), "expired", "Internal", "Ok", 1, "trace", "span", null, []));
        snapshot.AddLog(new LogSample(DateTime.UtcNow.AddMinutes(-10), "Error", "test", "older", null, null, null));
        snapshot.AddLog(new LogSample(DateTime.UtcNow, "Error", "test", "recent", null, null, null));
        snapshot.SetMetrics(Enumerable.Range(0, 1001).Select(index =>
            new MetricSample("metric" + index, "test", "s", "Sum", [], 1, null, null, null, null)));
        Assert.Equal(2, Read(snapshot, minutes: 999).GetProperty("matchingLogs").GetInt32());
        var json = Read(snapshot, minutes: 5, limit: -1);
        Assert.Equal(1, json.GetProperty("matchingLogs").GetInt32());
        Assert.Equal(0, json.GetProperty("matchingTraces").GetInt32());
        Assert.Equal(1000, json.GetProperty("metrics").GetArrayLength());
        Assert.Equal(1, json.GetProperty("omittedMetrics").GetInt32());
    }

    [Fact]
    public void Metric_exporter_preserves_cumulative_counters_histograms_and_gauges()
    {
        var snapshot = new ObservabilitySnapshot("test", "Testing");
        using var meter = new Meter("test." + Guid.NewGuid());
        using var provider = Sdk.CreateMeterProviderBuilder().AddMeter(meter.Name)
            .AddReader(new PeriodicExportingMetricReader(new SnapshotMetricExporter(snapshot), 60000)
            { TemporalityPreference = MetricReaderTemporalityPreference.Cumulative }).Build();
        var counter = meter.CreateCounter<long>("test.tokens", "token");
        var histogram = meter.CreateHistogram<double>("test.duration", "s");
        meter.CreateObservableGauge("test.memory", () => 42L, "By");
        meter.CreateObservableGauge("test.invalid", () => double.NaN);
        counter.Add(3, new KeyValuePair<string, object?>("llm.operation", "query"),
            new KeyValuePair<string, object?>("user.id", "secret-user"));
        histogram.Record(2);
        histogram.Record(4);
        Assert.True(provider.ForceFlush());
        Assert.True(provider.ForceFlush());
        var json = Read(snapshot);
        var metrics = json.GetProperty("metrics").EnumerateArray().ToArray();
        Assert.Equal(3, metrics.Single(metric => metric.GetProperty("name").GetString() == "test.tokens").GetProperty("value").GetDouble());
        var duration = metrics.Single(metric => metric.GetProperty("name").GetString() == "test.duration");
        Assert.Equal(2, duration.GetProperty("count").GetInt64());
        Assert.Equal(6, duration.GetProperty("sum").GetDouble());
        Assert.Equal(2, duration.GetProperty("min").GetDouble());
        Assert.Equal(4, duration.GetProperty("max").GetDouble());
        Assert.Equal(42, metrics.Single(metric => metric.GetProperty("name").GetString() == "test.memory").GetProperty("value").GetDouble());
        Assert.DoesNotContain("secret-user", json.ToString());
        Assert.Contains("llm.operation", json.ToString());
    }

    [Fact]
    public void Logs_and_traces_correlate_without_exporting_sensitive_payloads()
    {
        var snapshot = new ObservabilitySnapshot("test", "Testing");
        using var source = new ActivitySource("test." + Guid.NewGuid());
        using var tracing = Sdk.CreateTracerProviderBuilder().AddSource(source.Name)
            .AddProcessor(new SimpleActivityExportProcessor(new SnapshotTraceExporter(snapshot))).Build();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddProcessor(new SimpleLogRecordExportProcessor(new SnapshotLogExporter(snapshot)));
        }));
        var logger = loggerFactory.CreateLogger("test");
        using (var activity = source.StartActivity("query", ActivityKind.Client))
        {
            Assert.NotNull(activity);
            activity.SetTag("db.statement", "secret-sql");
            activity.SetTag("http.route", "/api/query/{id}");
            activity.SetStatus(ActivityStatusCode.Error, "secret-error");
            logger.LogError(new InvalidOperationException("secret-exception"), "Query failed for {User}", "secret-user");
        }
        using (var noise = source.StartActivity("poll")) noise?.SetTag("url.path", "/api/admin/observability");
        using (var noise = source.StartActivity("proxy-poll")) noise?.SetTag("url.full", "http://internal/api/admin/observability?minutes=15");
        logger.LogInformation("unstructured secret-message");
        var json = Read(snapshot);
        var trace = Assert.Single(json.GetProperty("traces").EnumerateArray());
        var logs = json.GetProperty("logs").EnumerateArray().ToArray();
        var failure = logs.Single(log => log.GetProperty("level").GetString() == "Error");
        Assert.Equal("Query failed for {User}", failure.GetProperty("message").GetString());
        Assert.Equal(trace.GetProperty("traceId").GetString(), failure.GetProperty("traceId").GetString());
        Assert.Equal("InvalidOperationException", failure.GetProperty("exceptionType").GetString());
        Assert.DoesNotContain("secret-", json.ToString());
    }

    [Fact]
    public async Task Endpoint_rejects_anonymous_and_company_users_and_disables_caching()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddGrafirioTelemetry("test");
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Bearer", _ => { });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGrafirioObservability();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/observability")).StatusCode);
        client.DefaultRequestHeaders.Add("Test-Role", "COMPANY_ADMIN");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/observability")).StatusCode);
        client.DefaultRequestHeaders.Remove("Test-Role");
        client.DefaultRequestHeaders.Add("Test-Role", "PLATFORM_ADMIN");
        using var response = await client.GetAsync("/api/admin/observability?minutes=5&limit=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("test", await response.Content.ReadAsStringAsync());
        await app.StopAsync();
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["Test-Role"].ToString();
            if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("business_roles", role)], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

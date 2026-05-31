using Grafirio.Gateway.Metrics;
using Grafirio.Shared.Extensions;
using Serilog;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);

// Serilog'u appsettings.json'dan okuyacak �ekilde yap�land�r
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddSingleton<RequestMetrics>();

builder.Services.AddAuthenticationAndAuthorizationExt(builder.Configuration);

var app = builder.Build();

// Global Exception Handling
app.UseCorrelationId();
app.UseGlobalExceptionHandler();

// Gateway'e gelen her isteği otomatik loglamak için bu middleware'i ekleyin
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy(pipeline =>
{
    pipeline.Use(async (ctx, next) =>
    {
        await next();
        var routeId = ctx.Features.Get<IReverseProxyFeature>()?.Route.Config.RouteId;
        if (routeId is not null)
            ctx.RequestServices.GetRequiredService<RequestMetrics>().Record(routeId, ctx.Response.StatusCode);
    });
});

app.MapGet("/", () => "YARP (Gateway)");
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "API Gateway", Timestamp = DateTime.UtcNow }));
app.MapGet("/metrics", (RequestMetrics m) => Results.Ok(m.GetAll()));
app.MapPost("/metrics/reset", (RequestMetrics m) => { m.Reset(); return Results.NoContent(); });

app.Run();
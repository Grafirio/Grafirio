using Grafirio.Gateway.Metrics;
using Grafirio.Shared.Infrastructure.Extensions;
using Grafirio.Shared.Identity.Extensions;
using Serilog;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);

// Serilog'u appsettings.json'dan okuyacak �ekilde yap�land�r
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddSingleton<RequestMetrics>();

builder.Services.AddAuthenticationAndAuthorizationExt(builder.Configuration);

// Tarayici, Authorization basligi tasiyan her capraz kaynak istegi icin once
// bir OPTIONS "preflight" gonderiyor ve o istege kimlik bilgisi KOYMUYOR.
// CORS hic yapilandirilmadigi icin preflight yetki katmanina kadar gidiyor,
// 401 ile donuyor ve Access-Control-Allow-Origin basligi hic eklenmiyor;
// tarayici da asil istegi gondermeden iptal ediyor. Yani panelden gateway'e
// yapilan cagrilarin tamami sunucuya ulasmadan oluyordu.
const string BrowserCorsPolicy = "browser-clients";

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(BrowserCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Kimlik Authorization basligiyla tasiniyor, cerezle degil; bu yuzden
        // AllowCredentials gerekmiyor ve origin listesi dar tutulabiliyor.
        .WithExposedHeaders("X-Correlation-Id"));
});

var app = builder.Build();

app.Use(Program.RejectInternalRequestsAsync);

// Global Exception Handling
app.UseCorrelationId();
app.UseGlobalExceptionHandler();

// Gateway'e gelen her isteği otomatik loglamak için bu middleware'i ekleyin
app.UseSerilogRequestLogging();

// Siralama onemli: CORS, yetkiden ONCE calismali. Aksi halde preflight istegi
// yetki katmanina yakalanip 401 doner ve tarayici cevabi hic okumaz.
app.UseCors(BrowserCorsPolicy);

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

public partial class Program
{
    private const string InternalDataAnalysisPath = "/data-analysis/internal";

    public static Task RejectInternalRequestsAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments(InternalDataAnalysisPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        // Reject ambiguous paths before a downstream URI parser can reinterpret them.
        var value = path.Value ?? string.Empty;
        if (value.Contains('%') || value.Contains('\\') || value.Contains("//", StringComparison.Ordinal)
            || value.Split('/').Any(segment => segment is "." or ".."))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
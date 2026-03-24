using Grafirio.Shared.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog'u appsettings.json'dan okuyacak �ekilde yap�land�r
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddAuthenticationAndAuthorizationExt(builder.Configuration);

var app = builder.Build();

// Global Exception Handling
app.UseCorrelationId();
app.UseGlobalExceptionHandler();

// Gateway'e gelen her isteği otomatik loglamak için bu middleware'i ekleyin
app.UseSerilogRequestLogging();

app.MapReverseProxy();
app.MapGet("/", () => "YARP (Gateway)");
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "API Gateway", Timestamp = DateTime.UtcNow }));
app.UseAuthentication();
app.UseAuthorization();

app.Run();
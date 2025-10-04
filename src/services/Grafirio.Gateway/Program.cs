using Grafirio.Shared.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog'u appsettings.json'dan okuyacak þekilde yapýlandýr
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddAuthenticationAndAuthorizationExt(builder.Configuration);

var app = builder.Build();

// Gateway'e gelen her isteði otomatik loglamak için bu middleware'i ekleyin
app.UseSerilogRequestLogging();

app.MapReverseProxy();
app.MapGet("/", () => "YARP (Gateway)");
app.UseAuthentication();
app.UseAuthorization();

app.Run();
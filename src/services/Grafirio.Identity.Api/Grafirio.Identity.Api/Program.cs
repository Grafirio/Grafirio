using Grafirio.Identity.Api;
using Grafirio.Identity.Api.Features.Companies;
using Grafirio.Identity.Api.Features.Subscriptions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Options;
using Grafirio.Identity.Api.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOptionsExt();
builder.Services.AddDatabaseServiceExt();
builder.Services.AddCommonServiceExt(typeof(IdentityAssembly));
builder.Services.AddIdentityServicesExt();
builder.Services.AddVersioningExt();
builder.Services.AddAuthenticationAndAuthorizationExt(builder.Configuration);
var app = builder.Build();

// Global Exception Handling & Logging
app.UseGlobalExceptionHandling();

app.AddSeedDataExt().ContinueWith(x =>
{
    Console.WriteLine(x.IsFaulted ? x.Exception?.Message : "Seed data has been saved successfully");
});
app.AddCompanyGroupEndpointExt(app.AddVersionSetExt());
app.AddUserGroupEndpointExt(app.AddVersionSetExt());
app.AddSubscriptionGroupEndpointExt(app.AddVersionSetExt());

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();
app.UseAuthorization();

// Health Check
app.MapGet("/health", () => Results.Ok(new 
{ 
    Status = "Healthy", 
    Service = "Identity API",
    Timestamp = DateTime.UtcNow 
}))
.WithName("HealthCheck")
.WithTags("Health");

app.Run();

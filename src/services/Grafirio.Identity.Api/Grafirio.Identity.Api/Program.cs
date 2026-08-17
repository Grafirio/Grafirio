using Grafirio.Identity.Api;
using Grafirio.Identity.Api.Features.Companies;
using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Companies.Documents;
using Grafirio.Identity.Api.Features.Departments;
using Grafirio.Identity.Api.Features.Subscriptions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Options;
using Grafirio.Identity.Api.Repositories;
using Grafirio.Shared.Infrastructure.MassTransit.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOptionsExt();
builder.Services.AddDatabaseServiceExt();
builder.Services.AddCompanyDocumentStorage();
// Yetki kaynagi: token claim'i degil veritabanindaki uyelik kayitlari.
builder.Services.AddScoped<ICompanyAccessService, CompanyAccessService>();
builder.Services.AddCommonServiceExt(typeof(IdentityAssembly));
builder.Services.AddIdentityServicesExt();
builder.Services.AddVersioningExt();
builder.Services.AddAuthenticationAndAuthorizationExt(builder.Configuration);

// Odeme alindiginda ticaret tarafinin duyurdugu olay dinlenir; satin almanin
// erisim hakkina donusup donusmeyecegine bu servis karar veriyor.
builder.Services.AddGrafirioMassTransit(builder.Configuration, x =>
{
    x.AddConsumer<OrderPaidConsumer>();
});

var app = builder.Build();

// Global Exception Handling & Logging
app.UseGlobalExceptionHandling();

// Tohum verisinden once ve bilerek beklenerek: bu onarim tamamlanmadan
// yapilan her sirket okumasi "mapped collection but missing" ile duser, yani
// arkasindan gelen her sey buna bagli. Seed'in ates-et-unut kalibi burada
// kullanilamaz.
await app.RepairCompanyEmbeddedListsExt();

app.AddSeedDataExt().ContinueWith(x =>
{
    Console.WriteLine(x.IsFaulted ? x.Exception?.Message : "Seed data has been saved successfully");
});
app.AddCompanyGroupEndpointExt(app.AddVersionSetExt());
app.AddDepartmentGroupEndpointExt(app.AddVersionSetExt());
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

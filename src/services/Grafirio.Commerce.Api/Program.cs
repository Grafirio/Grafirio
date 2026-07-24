using Asp.Versioning.Builder;
using FluentValidation;
using Grafirio.Commerce.Api.Modules.Basket;
using Grafirio.Commerce.Api.Modules.Catalog;
using Grafirio.Commerce.Api.Modules.Discount;
using Grafirio.Commerce.Api.Modules.Files;
using Grafirio.Commerce.Api.Modules.Orders;
using Grafirio.Commerce.Api.Modules.Payments;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddVersioningExt();
builder.Services.AddAuthenticationAndAuthorizationExt(builder.Configuration);
builder.Services.AddCommonServiceExt(typeof(Grafirio.Commerce.Api.CommerceAssembly));
builder.Services.AddIdentityServicesExt();

// ── Paylaşımlı MongoDB istemcisi (tek instance, farklı DB'ler) ───────────────
builder.Services.Configure<Grafirio.Commerce.Api.MongoOption>(
    builder.Configuration.GetSection(nameof(Grafirio.Commerce.Api.MongoOption)));
builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(sp.GetRequiredService<IOptions<Grafirio.Commerce.Api.MongoOption>>().Value.ConnectionString));

// ── Modüller ────────────────────────────────────────────────────────────────
builder.Services.AddBasketModule(builder.Configuration);
builder.Services.AddCatalogModule(builder.Configuration);
builder.Services.AddDiscountModule(builder.Configuration);
builder.Services.AddOrderModule(builder.Configuration);
builder.Services.AddPaymentModule();
builder.Services.AddFileModule();

var app = builder.Build();

app.UseGlobalExceptionHandling();

var versionSet = app.AddVersionSetExt();

// ── Endpoint'ler ────────────────────────────────────────────────────────────
app.MapBasketEndpoints(versionSet);
app.MapCatalogEndpoints(versionSet);
app.MapDiscountEndpoints(versionSet);
app.MapOrderEndpoints(versionSet);
app.MapPaymentEndpoints(versionSet);
app.MapFileEndpoints(versionSet);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    Status    = "Healthy",
    Service   = "Commerce API",
    Timestamp = DateTime.UtcNow
}));

app.Run();

using System.Text.Json;
using Grafirio.DataAnalysis.Api.Features.Connection;
using Grafirio.DataAnalysis.Api.Features.Schema;
using Grafirio.DataAnalysis.Api.Features.Analysis;
using Grafirio.DataAnalysis.Api.Features.AI;
using Grafirio.Shared.MassTransit.Extensions;

var builder = WebApplication.CreateBuilder(args);

// JSON serialization - camelCase için
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});

// Add services
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS - Frontend'den erişim için
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// MassTransit - RabbitMQ
builder.Services.AddGrafiiroMassTransit(builder.Configuration, x =>
{
    x.AddConsumer<DataAnalysisRequestConsumer>();  // ✅ Request'leri dinler ve AI analizi yapar
    x.AddConsumer<DataAnalysisResponseConsumer>(); // ✅ Response'ları dinler (opsiyonel)
});

var app = builder.Build();

// Configure HTTP pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Data Analysis API V1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowAll");

// Map endpoints
app.MapConnectionEndpoints();
app.MapSchemaEndpoints();
app.MapAnalysisEndpoints();
app.MapAIAnalysisEndpoints();
app.MapSchemaDiscoveryEndpoints();
app.MapAIReportEndpoints();
app.MapTestDjangoEndpoints(); // 🧪 Test endpoint

// Health check
app.MapGet("/health", () => Results.Ok(new { 
    Status = "Healthy", 
    Service = "Data Analysis API",
    Timestamp = DateTime.UtcNow 
}))
.WithName("HealthCheck")
.WithOpenApi();

app.Run();

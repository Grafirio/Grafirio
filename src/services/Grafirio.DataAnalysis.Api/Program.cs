using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Features.Connection;
using Grafirio.DataAnalysis.Api.Features.Schema;
using Grafirio.DataAnalysis.Api.Features.Analysis;
using Grafirio.DataAnalysis.Api.Features.AI;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Shared.MassTransit.Extensions;
using Grafirio.Shared.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database Context - PostgreSQL
builder.Services.AddDbContext<DataAnalysisDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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

// Gemini LLM Service
builder.Services.AddSingleton<GeminiService>();

// Query Result Store (in-memory cache for AI results)
builder.Services.AddSingleton<QueryResultStore>();

// HttpClientFactory — PyCaret Engine çağrıları için
builder.Services.AddHttpClient();

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

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();
        db.Database.EnsureCreated(); // Creates database and tables if not exist
        app.Logger.LogInformation("✅ Database initialized successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "❌ Database initialization failed");
    }
}

// Global Exception Handling & Logging
app.UseGlobalExceptionHandling();

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
app.MapConnectionEndpoints(); // Test connection
app.MapSavedConnectionEndpoints(); // Saved connections CRUD
app.MapSchemaEndpoints();
app.MapAnalysisEndpoints();
app.MapAIAnalysisEndpoints();
app.MapSchemaDiscoveryEndpoints();
app.MapAIReportEndpoints();
app.MapQueryResultEndpoints(); // Query result & progress tracking
app.MapAgentAnalyzeEndpoints(); // AI Agent — schema analiz
app.MapAgentQueryEndpoints(); // AI Agent — sorgu ve PyCaret
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

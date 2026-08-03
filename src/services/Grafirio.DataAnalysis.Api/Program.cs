using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Features.Connection;
using Grafirio.DataAnalysis.Api.Features.Schema;
using Grafirio.DataAnalysis.Api.Features.Analysis;
using Grafirio.DataAnalysis.Api.Features.AI;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Shared.Infrastructure.MassTransit.Extensions;
using Grafirio.Contracts.AI;
using Grafirio.Shared.Infrastructure.Extensions;
using Grafirio.Shared.Identity.Extensions;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

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
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ILlmClient, LlmClient>();
builder.Services.AddSingleton<GeminiService>();

// Redis — QueryResultStore + diğer servisler
var redisConnectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString")
    ?? Environment.GetEnvironmentVariable("REDIS__CONNECTIONSTRING")
    ?? "localhost:6379,abortConnect=false";
try
{
    var redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
    builder.Services.AddSingleton<QueryResultStore>();
}
catch (Exception redisEx)
{
    // Redis bağlanamıyorsa uygulama yine çalışsın, QueryResultStore devre dışı kalır
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => throw new InvalidOperationException("Redis bağlanamadı: " + redisEx.Message));
    builder.Services.AddSingleton<QueryResultStore>(); // hata fırlatacak ama diğer endpointler çalışır
}

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
builder.Services.AddGrafirioMassTransit(
    builder.Configuration,
    x =>
    {
        x.AddConsumer<DataAnalysisRequestConsumer>();  // Analiz request'lerini alır, Django'ya iletir
        x.AddConsumer<DataAnalysisResponseConsumer>(); // Analiz response'larını dinler
        // QuestionRequestConsumer kaldırıldı — bridge anti-pattern
    },
    cfg =>
    {
        // IQuestionRequest → Django'nun 'ai.requests' (fanout) exchange'ine yayınla
        // Django bu exchange'e bağlı 'django.ai.requests' kuyruğunu dinliyor
        cfg.Message<IQuestionRequest>(m => m.SetEntityName("ai.requests"));
        cfg.Publish<IQuestionRequest>(p => p.ExchangeType = RabbitMQ.Client.ExchangeType.Fanout);
    });

// Kimlik dogrulama. Bu servis daha once hic kurmamisti: uclar acikta duruyor
// ve kullanici kimligini sorgu dizesinden aliyordu, yani isteyen istedigi
// userId ile baskasinin kayitli baglantilarini okuyabiliyordu.
builder.Services.AddAuthenticationAndAuthorizationExt(builder.Configuration);

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

app.UseAuthentication();
app.UseAuthorization();

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

using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using MongoDB.Driver;
using Grafirio.DataAnalysis.Api.Features.Connection;
using Grafirio.DataAnalysis.Api.Features.Schema;
using Grafirio.DataAnalysis.Api.Features.Analysis;
using Grafirio.DataAnalysis.Api.Features.AI;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.DataAnalysis.Api.Features.Profile;
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
builder.Services.AddSingleton<SchemaProfiler>();

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

// MongoDB — tablo secimi ve sema profili (kalici)
// Postgres semasi EnsureCreated ile kuruluyor ve migration yok; profil de
// dokuman yapisinda oldugu icin burada tutuluyor.
var mongoConnectionString = builder.Configuration.GetValue<string>("Mongo:ConnectionString")
    ?? Environment.GetEnvironmentVariable("MONGO__CONNECTIONSTRING");
var mongoDatabaseName = builder.Configuration.GetValue<string>("Mongo:DatabaseName")
    ?? Environment.GetEnvironmentVariable("MONGO__DATABASENAME")
    ?? "GrafirioDataAnalysisDb";

if (!string.IsNullOrWhiteSpace(mongoConnectionString))
{
    builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDatabaseName));
    builder.Services.AddSingleton<ConnectionProfileStore>();
}
else
{
    // Baglanti dizesi yoksa servis yine ayaga kalksin; yalnizca profil
    // ucları calismasin. Cozumleme aninda sebebi yazan acik bir hata veriyor —
    // sessizce bos donen bir depo, teshis edilemeyen hatalara yol aciyordu.
    builder.Services.AddSingleton<IMongoDatabase>(_ => throw new InvalidOperationException(
        "Mongo__ConnectionString tanımlı değil; tablo seçimi ve şema profili kullanılamaz."));
    builder.Services.AddSingleton<ConnectionProfileStore>();
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

// IIdentityService (token'daki company_id / userId'yi okuyan servis) burada
// kayitli degildi; Commerce ve Identity servisleri bunu yapiyor, bu servis
// atlamis. Sonuc: sirket suzgeci kullanan her uc "No service for type
// IIdentityService" ile 400 donuyordu. IdentityService HttpContext'e
// bagimli oldugu icin accessor da gerekiyor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddIdentityServicesExt();

var app = builder.Build();

// Auto-migrate database on startup
// Sifreleme anahtari acilista dogrulanir. Eksikse servis hic ayaga kalkmasin:
// anahtarsiz calismak, musteri veritabani sifrelerini herkesin bildigi bir
// varsayilanla sifrelemek demekti ve bu sessizce oluyordu.
EncryptionHelper.EnsureConfigured();
app.Logger.LogInformation("✅ Şifreleme anahtarı yapılandırılmış");

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
app.MapTableSelectionEndpoints(); // Secili tablolar (kural: yalnizca bunlar islenir)
app.MapPreAnalysisEndpoints();    // On analiz: profil + semantik sozluk + bekletici kapi
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
// Onceki surum kosulsuz "Healthy" donuyordu — hicbir bagimliligi yoklamadigi
// icin Redis de LLM de dusmusken bile yesil gorunuyordu. Bir arizada bakilacak
// ilk yer burasi oldugu halde hicbir sey soylemiyordu; LLM yapilandirmasi
// hatasinin teshisi bu yuzden loglari tek tek okumaya kaldi.
app.MapGet("/health", async (
    DataAnalysisDbContext db,
    ILlmClient llm,
    IServiceProvider services,
    IConfiguration configuration) =>
{
    var checks = new Dictionary<string, object>();
    var healthy = true;

    async Task Probe(string name, Func<Task> action)
    {
        try
        {
            await action();
            checks[name] = new { status = "ok" };
        }
        catch (Exception ex)
        {
            healthy = false;
            checks[name] = new { status = "fail", error = ex.Message };
        }
    }

    await Probe("postgres", async () =>
    {
        if (!await db.Database.CanConnectAsync())
            throw new InvalidOperationException("Bağlantı kurulamadı");
    });

    await Probe("redis", async () =>
    {
        // Redis acilista baglanamadiysa kayit hata firlatan bir fabrikaya
        // baglanmis oluyor; cozumleme burada patlar ve sebebi gorunur.
        var mux = services.GetRequiredService<IConnectionMultiplexer>();
        await mux.GetDatabase().PingAsync();
    });

    // LLM icin gercek bir cagri yapilmiyor: her saglik yoklamasinda token
    // harcamak istemiyoruz. Yalnizca saglayicinin secili ve anahtarinin
    // tanimli olup olmadigi bildiriliyor — asil kacirilan sey buydu.
    var provider = configuration["LLM_PROVIDER"] ?? configuration["Llm:Provider"] ?? "(tanımsız → azure_openai)";
    if (!llm.IsConfigured) healthy = false;
    checks["llm"] = new
    {
        status = llm.IsConfigured ? "ok" : "fail",
        provider,
        error = llm.IsConfigured ? null : "API anahtarı tanımlı değil — sorgu çevirisi çalışmaz"
    };

    var payload = new
    {
        status = healthy ? "Healthy" : "Degraded",
        service = "Data Analysis API",
        checks,
        timestamp = DateTime.UtcNow
    };

    // Bagimliligi dusmus bir servis "ayakta" sayilmamali; ACA ve izleme
    // araclari 200'u saglikli kabul ediyor.
    return healthy ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("HealthCheck")
.WithOpenApi();

app.Run();

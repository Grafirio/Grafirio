using System.Text.Json;
using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using StackExchange.Redis;
using MongoDB.Driver;
using Grafirio.DataAnalysis.Api.Features.Schema;
using Grafirio.DataAnalysis.Api.Features.Analysis;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Features.Connections;
using Grafirio.DataAnalysis.Api.Features.Internal;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Shared.Infrastructure.Extensions;
using Grafirio.Shared.Infrastructure.MassTransit.Extensions;
using MassTransit;
using Grafirio.Shared.Identity.Extensions;
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

// LLM — Azure OpenAI.
//
// Kendi adli istemcisi var cunku varsayilan 100 saniyelik zaman asimi bu is
// icin yetmiyordu: onlarca tablonun sozlugunu ureten cagri, reasoning
// adimlariyla birlikte dakikalari buluyor ve istek tam da model cevabi
// yazarken iptal ediliyordu. Disaridan gorunen sey "The request was canceled
// due to the configured HttpClient.Timeout of 100 seconds elapsing" oluyordu —
// yani LLM'den degil, kendi istemcimizden gelen bir hata.
builder.Services.AddHttpClient(nameof(LlmClient), client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ILlmClient, LlmClient>();
builder.Services.AddSingleton<LlmAnalysisService>();
builder.Services.AddSingleton<RelationshipDiscovery>();
builder.Services.AddSingleton<SchemaProfiler>();

// Musteri veritabanina giden tek kapi. Iki yol var: buluttan dogrudan TCP,
// ya da musteri agindaki bridge uzerinden. Secimi fabrika yapiyor; cagri
// noktalarinin hicbiri farki gormuyor.
builder.Services.AddSingleton<IDataSourceFactory, DataSourceFactory>();

// Bridge: musteri agindan disari dogru kurulan kanal.
builder.Services.AddSingleton<BridgeRegistry>();

var signalR = builder.Services.AddSignalR(options =>
{
    // Satirlar parcalar halinde geliyor; varsayilan 32 KB tavani genis
    // tablolarda tek bir parcaya bile yetmiyor.
    options.MaximumReceiveMessageSize = 4 * 1024 * 1024;
});

// Cok replikali calisma iki ayri seye ihtiyac duyuyor ve ikisi de Redis'e
// bagli; Redis yapilandirilmamissa servis tek replika varsayimiyla calisir.
//
//   1. SignalR backplane — sorgu istegi, bridge hangi replikaya bagliysa
//      oraya ulassin.
//   2. Cevap otobüsü — bridge'in cevabi, sorguyu baslatan replikaya donsun.
//      Backplane bunu tasimaz; yalnizca sunucudan istemciye gideni tasir.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__REDIS");

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    signalR.AddStackExchangeRedis(redisConnectionString);

    builder.Services.AddSingleton<IConnectionMultiplexer>(
        _ => ConnectionMultiplexer.Connect(redisConnectionString));
    builder.Services.AddSingleton<IBridgeResponseBus, RedisBridgeResponseBus>();
}
else
{
    builder.Services.AddSingleton<IBridgeResponseBus, InProcessBridgeResponseBus>();
}

// MongoDB — tablo secimi (kalici)
// Postgres semasi EnsureCreated ile kuruluyor ve migration yok; secim de
// degisken uzunlukta bir liste oldugu icin burada tutuluyor.
var mongoConnectionString = builder.Configuration.GetValue<string>("Mongo:ConnectionString")
    ?? Environment.GetEnvironmentVariable("MONGO__CONNECTIONSTRING");
var mongoDatabaseName = builder.Configuration.GetValue<string>("Mongo:DatabaseName")
    ?? Environment.GetEnvironmentVariable("MONGO__DATABASENAME")
    ?? "GrafirioDataAnalysisDb";

// Kullanici adi ve sifre AYRI verilebiliyor.
//
// Sebebi somut: baglanti dizesi bir URI ve sifre '@', '/', ':' gibi bir
// karakter iceriyorsa dize gecersiz oluyor — "The connection string ... is not
// valid" hatasi, sifrenin yanlis oldugunu degil URI'nin bozuk oldugunu
// soyluyor ve bu ayrim loglardan anlasilmiyor. Yerel compose tam da bu yuzden
// hic calismamisti. Ayri alanlar verildiginde kaciş isi surucunun kendisine
// birakiliyor ve sifre secimi bir yapilandirma tuzagi olmaktan cikiyor.
var mongoUsername = builder.Configuration.GetValue<string>("Mongo:Username")
    ?? Environment.GetEnvironmentVariable("MONGO__USERNAME");
var mongoPassword = builder.Configuration.GetValue<string>("Mongo:Password")
    ?? Environment.GetEnvironmentVariable("MONGO__PASSWORD");

if (!string.IsNullOrWhiteSpace(mongoConnectionString))
{
    builder.Services.AddSingleton<IMongoClient>(_ =>
    {
        if (string.IsNullOrEmpty(mongoUsername) || string.IsNullOrEmpty(mongoPassword))
            return new MongoClient(mongoConnectionString);

        var settings = MongoClientSettings.FromConnectionString(mongoConnectionString);
        settings.Credential = MongoCredential.CreateCredential(
            databaseName: "admin", mongoUsername, mongoPassword);

        return new MongoClient(settings);
    });
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDatabaseName));
    builder.Services.AddSingleton<ConnectionProfileStore>();
    builder.Services.AddSingleton<LearnedFactStore>();
    builder.Services.AddSingleton<BridgeStore>();
    builder.Services.AddSingleton<IBridgePresence>(sp => sp.GetRequiredService<BridgeStore>());
    // Baglanti tanimlarini bridge'e iten servis. Mongo'ya bagli oldugu icin
    // yalnizca Mongo yapilandirilmissa kayitli.
    builder.Services.AddSingleton<BridgeConnectionSync>();
}
else
{
    // Baglanti dizesi yoksa servis yine ayaga kalksin; yalnizca profil
    // ucları calismasin. Cozumleme aninda sebebi yazan acik bir hata veriyor —
    // sessizce bos donen bir depo, teshis edilemeyen hatalara yol aciyordu.
    builder.Services.AddSingleton<IMongoDatabase>(_ => throw new InvalidOperationException(
        "Mongo__ConnectionString tanımlı değil; tablo seçimi ve şema profili kullanılamaz."));
    builder.Services.AddSingleton<ConnectionProfileStore>();
    builder.Services.AddSingleton<LearnedFactStore>();
    builder.Services.AddSingleton<BridgeStore>();
    builder.Services.AddSingleton<IBridgePresence>(sp => sp.GetRequiredService<BridgeStore>());
    // Baglanti tanimlarini bridge'e iten servis. Mongo'ya bagli oldugu icin
    // yalnizca Mongo yapilandirilmissa kayitli.
    builder.Services.AddSingleton<BridgeConnectionSync>();
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

// MassTransit — "Analiz Et" isini kuyruga alir.
//
// Django hattina hizmet eden eski consumer'lar kaldirildi; kuyrugun kendisi
// duruyor cunku analiz uzun suren bir is: profil cikarma arti LLM cagrisi
// dakikalar aliyor. Istek icinde yapilinca gateway 504 veriyor, `Task.Run`
// ile yapilinca container yeniden baslarsa is sessizce kayboluyor ve kayit
// sonsuza kadar "analyzing" kaliyordu. Kuyruk isi dayanikli kiliyor,
// yeniden deneme de gecici Azure hatalarini karsiliyor.
builder.Services.AddGrafirioMassTransit(
    builder.Configuration,
    x => x.AddConsumer<ConnectionAnalysisConsumer>(),
    cfg =>
    {
        // Yeniden deneme sayisi tuketicideki MaxRetries ile ayni olmali:
        // tuketici son denemede hatayi yutup kaydi "failed" isaretliyor.
        cfg.UseMessageRetry(r => r.Interval(
            ConnectionAnalysisConsumer.MaxRetries, TimeSpan.FromSeconds(30)));
    });

// Kimlik dogrulama. Bu servis daha once hic kurmamisti: uclar acikta duruyor
// ve kullanici kimligini sorgu dizesinden aliyordu, yani isteyen istedigi
// userId ile baskasinin kayitli baglantilarini okuyabiliyordu.
builder.Services.AddAuthenticationAndAuthorizationExt(builder.Configuration);

// Varsayilan forbid/challenge semasi. Paylasilan kurulum AddAuthentication()'i
// varsayilan sema VERMEDEN cagiriyor; adli politikalar semayi kendileri
// belirttigi icin bu, yetkilendirme icin sorun degil. Sorun reddetme yolunda:
//
//   RequirePermission(...) izin yoksa `Results.Forbid()` donuyor — semasiz.
//   Semasiz Forbid varsayilani ariyor, bulamayinca
//   "No authenticationScheme was specified, and there was no DefaultForbidScheme
//   found" ile ISTISNA firlatiyor. Global hata middleware'i bunu 400'e
//   ceviriyor ve govdeye hicbir sey yazmiyor.
//
// Yani her yetki reddi, sebebini soylemeyen bir "400" olarak gorunuyordu:
// yetkisi olmayan kullanici "Request failed with status code 400" goruyor,
// gelistirici de teshis edecek hicbir sey bulamiyordu. Sema verilince
// reddetme dogru kodu (403) donuyor.
builder.Services.Configure<AuthenticationOptions>(options =>
{
    options.DefaultForbidScheme ??= JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme ??= JwtBearerDefaults.AuthenticationScheme;
});

// Bridge'ler de Keycloak token'i tasiyor — kendilerine ait bir client olarak,
// client_credentials ile. Ayri bir kimlik dogrulama semasi YOK; tek fark
// token'da bridge_id claim'inin bulunmasi.
//
// Onceki surumde burada elle yazilmis bir sema vardi: kendi token'imiz, kendi
// SHA256 ozetimiz. Auth sunucusu zaten kuruluydu; token suresi, anahtar
// rotasyonu ve merkezi iptal o surumde hic yoktu.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(BridgeAuthentication.Policy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(BridgeAuthentication.BridgeIdClaim)
        .RequireClaim(BridgeAuthentication.CompanyIdClaim));

builder.Services.AddSingleton<KeycloakBridgeIdentity>();
builder.Services.AddSingleton<BridgeInstaller>();

// IIdentityService (token'daki company_id / userId'yi okuyan servis) burada
// kayitli degildi; Commerce ve Identity servisleri bunu yapiyor, bu servis
// atlamis. Sonuc: sirket suzgeci kullanan her uc "No service for type
// IIdentityService" ile 400 donuyordu. IdentityService HttpContext'e
// bagimli oldugu icin accessor da gerekiyor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddIdentityServicesExt();

// Aksiyon seviyesinde yetki: "sirkete erisimi var mi" ile "baglantiyi
// degistirebilir mi" ayri sorular ve bu servis simdiye kadar yalnizca ilkini
// soruyordu. Departman matrisinde DATA_SOURCES.UPDATE'i kapatmak burada hicbir
// sey degistirmiyordu.
//
// Cevap Identity'den geliyor, token'dan degil: izinler claim'e yazilsaydi bir
// kullanicinin yetkisini almak token suresi kadar gecikirdi.
builder.Services.AddGrafirioPermissions(builder.Configuration);

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

        // EnsureCreated var olan tabloya dokunmuyor: yeni kolon yalnizca bos
        // veritabaninda olusur. Eksik kolonlar burada elle ekleniyor.
        SchemaPatches.Apply(db, app.Logger);

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
app.MapConnectionTestEndpoints();  // Baglanti denemesi
app.MapSavedConnectionEndpoints(); // Kayitli baglantilarin CRUD'u
app.MapTableSelectionEndpoints();  // Secili tablolar (kural: yalnizca bunlar islenir)
app.MapSchemaEndpoints();          // Tablo ve kolon listesi
app.MapAnalysisEndpoints();        // Veri kalitesi / istatistik / iliskiler
app.MapAgentAnalyzeEndpoints();    // Analiz Et: profil + semantik sozluk + sorular
app.MapAgentQueryEndpoints();      // Sorgu: soru -> parametre -> PyCaret
app.MapInternalDataEndpoints();    // PyCaret'in veri okudugu ic uc (gateway'e tanimlanmaz)
app.MapBridgeEndpoints();          // Bridge kaydi ve yonetimi

// Musteri agindaki bridge'lerin bagli durdugu kanal. Baglantiyi bridge kurar;
// sunucu hicbir zaman musteri agina baglanmaya calismaz.
app.MapHub<BridgeHub>(BridgeProtocol.HubPath);

// Cevap yolunu bagla: bu ornege yonlendirilen cevaplar bekleyen sorgulara
// ulassin. Baglanmazsa sorgular sessizce zaman asimina ugrardi.
app.Services.GetRequiredService<BridgeRegistry>().Start();

// Health check
// Onceki surum kosulsuz "Healthy" donuyordu — hicbir bagimliligi yoklamadigi
// icin LLM dusmusken bile yesil gorunuyordu. Bir arizada bakilacak ilk yer
// burasi oldugu halde hicbir sey soylemiyordu; LLM yapilandirmasi hatasinin
// teshisi bu yuzden loglari tek tek okumaya kaldi.
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

    await Probe("mongo", async () =>
    {
        // Mongo baglanti dizesi tanimsizsa kayit hata firlatan bir fabrikaya
        // baglanmis oluyor; cozumleme burada patlar ve sebebi gorunur.
        // Tablo secimi burada durdugu icin Mongo dustugunde analiz baslamaz.
        var database = services.GetRequiredService<IMongoDatabase>();
        await database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
    });

    // LLM icin gercek bir cagri yapilmiyor: her saglik yoklamasinda token
    // harcamak istemiyoruz. Yalnizca anahtarin tanimli olup olmadigi
    // bildiriliyor — asil kacirilan sey buydu.
    if (!llm.IsConfigured) healthy = false;
    checks["llm"] = new
    {
        status = llm.IsConfigured ? "ok" : "fail",
        deployment = configuration["AZURE_OPENAI_DEPLOYMENT"] ?? configuration["AzureOpenAI:Deployment"] ?? "(tanımsız)",
        error = llm.IsConfigured ? null : "AZURE_OPENAI_API_KEY tanımlı değil — analiz ve sorgu çalışmaz"
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

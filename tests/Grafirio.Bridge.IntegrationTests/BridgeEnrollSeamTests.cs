using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.EntityFrameworkCore;
using Grafirio.Shared.Identity.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Kurulumun onaydan SONRAKİ yarısı: kuran kişinin token'ıyla
/// <c>/api/bridges/enroll</c>'a gidip bridge'in kendi kimliğini alması.
///
/// Bu dikiş hiçbir yerde ölçülmüyordu. <see cref="BridgeDeviceLoginTests"/>
/// onay ÖNCESİNİ ölçüyor (kod veriliyor mu, onaysız token verilmiyor mu),
/// <see cref="BridgeEnrollmentTests"/> ise defteri. Aradaki adım — insanın
/// verdiği onayın bir makine kimliğine dönüşmesi — kurulumun tamamının
/// bağlı olduğu adım ve testsizdi.
///
/// Kimlik doğrulama burada TAKLİT EDİLMİYOR: üretimdeki
/// <c>AddAuthenticationAndAuthorizationExt</c> ve gerçek <c>CompanyAccess</c>
/// politikası kullanılıyor. Ölçülmek istenen şeylerden biri tam olarak o
/// politikanın davranışı.
///
/// <b>Adres ile issuer bilerek ayrı veriliyor.</b> Keycloak'a
/// <c>127.0.0.1</c> üzerinden ulaşılıyor ama doğrulanan issuer
/// <c>localhost</c>. Bu, compose'daki durumun birebir küçük hâli: servisler
/// Keycloak'a servis adıyla ulaşır, token ise dışarıdan — tarayıcıdan ya da
/// müşterinin ağındaki bir bridge'den — alınır. İkisinin ayrılabildiği
/// ölçülmezse, sahada yalnızca 401 olarak görünür.
///
///     docker compose up -d keycloak mongo.db
/// </summary>
public class BridgeEnrollSeamTests(KeycloakFixture keycloak, MongoFixture mongo)
    : IClassFixture<KeycloakFixture>, IClassFixture<MongoFixture>, IAsyncLifetime
{
    /// <summary>
    /// Servislerin Keycloak'a ULAŞTIĞI adres. Doğruladıkları issuer
    /// (<see cref="KeycloakFixture.Authority"/>) bundan farklı; bu testin
    /// koruduğu ayrım bu.
    /// </summary>
    private const string Backchannel = "http://127.0.0.1:8080/realms/grafirio";

    private const string InstallerClientId = "grafirio-client";

    private readonly List<string> _users = [];
    private readonly List<Guid> _bridges = [];

    // ---- Sunucu -----------------------------------------------------------

    private IHost BuildServer(string url) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseUrls(url)
                .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Error))
                .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(Settings()))
                .ConfigureServices((context, services) =>
                {
                    services.AddEndpointsApiExplorer();
                    services.AddHttpClient();
                    services.AddHttpContextAccessor();

                    // Üretimdeki kimlik doğrulama ve yetkilendirme — taklidi
                    // değil kendisi. "CompanyAccess" politikası burada doğuyor.
                    services.AddAuthenticationAndAuthorizationExt(context.Configuration);
                    services.AddIdentityServicesExt();

                    services.AddSingleton(new BridgeStore(
                        mongo.Database, NullLogger<BridgeStore>.Instance));
                    services.AddSingleton<KeycloakBridgeIdentity>();

                    // MapBridgeEndpoints yalnizca /enroll'u degil grubun
                    // tamamini kuruyor ve minimal API, yol tablosunu kurarken
                    // HER ucun parametrelerini cozumluyor. Kayitli olmayan bir
                    // tip "govde" sayilip yol kurulumu patliyor — bu yuzden
                    // cagrilmayan uclarin bagimliliklari da burada.
                    services.AddSignalR();
                    services.AddSingleton<IBridgeResponseBus, InProcessBridgeResponseBus>();
                    services.AddSingleton<BridgeRegistry>();
                    services.AddSingleton<IBridgePresence, NoopPresence>();
                    services.AddSingleton(new ConnectionProfileStore(
                        mongo.Database, NullLogger<ConnectionProfileStore>.Instance));
                    services.AddSingleton<BridgeConnectionSync>();

                    // Yalnizca cozumlenebilir olmasi yetiyor: baglanti
                    // baglama ucu bu testte hic cagrilmadigi icin nesne
                    // kurulmuyor, dolayisiyla Postgres'e gidilmiyor.
                    services.AddDbContext<DataAnalysisDbContext>(o =>
                        o.UseNpgsql("Host=yok;Database=yok;Username=yok;Password=yok"));
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapBridgeEndpoints());
                }))
            .Build();

    private Dictionary<string, string?> Settings() => new()
    {
        ["IdentityOption:Address"] = Backchannel,
        ["IdentityOption:Issuer"] = KeycloakFixture.Authority,
        ["IdentityOption:Audience"] = KeycloakFixture.Audience,
        ["Keycloak:BaseUrl"] = KeycloakFixture.BaseUrl,
        ["Keycloak:Realm"] = KeycloakFixture.Realm,
        ["Keycloak:AdminUsername"] = keycloak.AdminUsername,
        ["Keycloak:AdminPassword"] = keycloak.AdminPassword,
    };

    // ---- Testler ----------------------------------------------------------

    /// <summary>
    /// Onaysız kayıt yok.
    ///
    /// Kurulum dosyası herkese açık durabiliyor; bir bridge'i şirkete bağlayan
    /// tek şey kuran kişinin onayı. Bu uç kimlik doğrulaması istemeseydi,
    /// dosyayı eline geçiren herkes şirkete bridge tanıtabilirdi.
    /// </summary>
    [SkippableFact]
    public async Task Onay_olmadan_kayit_yapilamiyor()
    {
        Skip.IfNot(Ready, SkipReason);

        var url = $"http://127.0.0.1:{FreePort()}";
        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient();
            var response = await EnrollAsync(client, url, token: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// Kurulumun tamamı: kuran kişinin token'ı → bridge'in KENDİ kimliği.
    ///
    /// Dönen kimlikle gerçekten token alınabildiği de ölçülüyor. Alınamasaydı
    /// kurulum "başarılı" görünür, bridge bir sonraki adımda sessizce dışarıda
    /// kalırdı — ve bu, kurulumdan saatler sonra fark edilirdi.
    ///
    /// Alınan kimliğin kuran kişiyle bağı yok: o şirketten ayrıldığında bridge
    /// çalışmaya devam eder. Vaat edilen şey bu.
    /// </summary>
    [SkippableFact]
    public async Task Kuran_kisinin_onayi_bridge_kimligine_donusuyor()
    {
        Skip.IfNot(Ready, SkipReason);

        var companyId = Guid.NewGuid().ToString();
        var token = await InstallerTokenAsync(companyId);

        var url = $"http://127.0.0.1:{FreePort()}";
        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient();
            var response = await EnrollAsync(client, url, token);

            Assert.True(response.IsSuccessStatusCode,
                $"Kayıt reddedildi ({response.StatusCode}): " +
                await response.Content.ReadAsStringAsync());

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            var bridgeId = body.GetProperty("bridgeId").GetGuid();
            _bridges.Add(bridgeId);

            // Şirket İSTEKTEN değil token'dan: aşağıdaki istek şirket bilgisi
            // taşımıyor, cevaptaki değer kuran kişinin claim'inden geliyor.
            Assert.Equal(companyId, body.GetProperty("companyId").GetString());

            var credentials = new BridgeCredentials(
                body.GetProperty("clientId").GetString()!,
                body.GetProperty("clientSecret").GetString()!,
                body.GetProperty("tokenEndpoint").GetString()!);

            // Kimlik gerçekten çalışıyor mu — kurulumun kanıtı bu.
            Assert.NotEmpty(await BridgeTokenAsync(credentials));

            // Defterde de duruyor: panelin göstereceği kayıt.
            var store = new BridgeStore(mongo.Database, NullLogger<BridgeStore>.Instance);
            var record = await store.FindAsync(bridgeId);

            Assert.NotNull(record);
            Assert.Equal(companyId, record.CompanyId);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// Şirketi olmayan bir hesap bridge tanıtamıyor.
    ///
    /// Geçerli bir Grafirio hesabı olan herkes device flow'u tamamlayabilir;
    /// kayıt için yetmediğini ölçen yer burası. Hangi durum kodunun döndüğü
    /// politikanın işi — burada ölçülen şey deftere HİÇBİR ŞEY yazılmadığı.
    /// </summary>
    [SkippableFact]
    public async Task Sirketi_olmayan_hesap_bridge_tanitamiyor()
    {
        Skip.IfNot(Ready, SkipReason);

        var token = await InstallerTokenAsync(companyId: null);

        var url = $"http://127.0.0.1:{FreePort()}";
        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient();
            var response = await EnrollAsync(client, url, token);

            Assert.False(response.IsSuccessStatusCode,
                "Şirket bilgisi olmayan bir hesapla bridge kaydı yapıldı.");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// Uyumsuz sürüm, kimlik AÇILMADAN reddediliyor.
    ///
    /// Sıra önemli: sürüm kontrolü Keycloak'ta client açmadan önce yapılmazsa,
    /// her uyumsuz kurulum denemesi realm'de çöp bir kimlik bırakır.
    /// </summary>
    [SkippableFact]
    public async Task Uyumsuz_surum_kimlik_acilmadan_reddediliyor()
    {
        Skip.IfNot(Ready, SkipReason);

        var token = await InstallerTokenAsync(Guid.NewGuid().ToString());

        var url = $"http://127.0.0.1:{FreePort()}";
        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient();
            var response = await EnrollAsync(client, url, token, protocolVersion: "999");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// <b>Issuer, isteğin geldiği host'a göre değişmiyor.</b>
    ///
    /// Keycloak varsayılan olarak <c>iss</c>'i isteğin host'undan üretiyor:
    /// aynı realm, konteyner içinden <c>keycloak:8080</c>, dışarıdan
    /// <c>localhost:8080</c>. Servisler tek bir issuer doğruladığı için, o
    /// realm'e dışarıdan alınmış her token reddediliyordu — ve bridge bu yolu
    /// tanımı gereği HER ZAMAN dışarıdan yürüyor.
    ///
    /// Kodda hata gibi görünmüyor; yalnızca 401 olarak görünüyor. Ölçülen şey
    /// compose'daki <c>KC_HOSTNAME</c> ayarının yerinde durduğu.
    /// </summary>
    [SkippableFact]
    public async Task Issuer_istegin_geldigi_host_a_gore_degismiyor()
    {
        Skip.IfNot(keycloak.Available, keycloak.SkipReason);

        using var client = new HttpClient();

        var request = new HttpRequestMessage(
            HttpMethod.Get, $"{Backchannel}/.well-known/openid-configuration");

        // Konteyner içinden gelen isteğin taklidi: farklı host, aynı realm.
        request.Headers.Host = "keycloak:8080";

        var response = await client.SendAsync(request);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            KeycloakFixture.Authority,
            document.GetProperty("issuer").GetString());
    }

    // ---- Yardımcılar ------------------------------------------------------

    private bool Ready => keycloak.Available && mongo.Available;

    private string SkipReason =>
        !keycloak.Available ? keycloak.SkipReason : mongo.SkipReason;

    private static Task<HttpResponseMessage> EnrollAsync(
        HttpClient client, string url, string? token, string? protocolVersion = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"{url}/api/bridges/enroll")
        {
            Content = JsonContent.Create(new
            {
                machineName = "SRV-TEST",
                bridgeVersion = "1.0.0",
                protocolVersion = protocolVersion ?? BridgeProtocol.Version,
                name = "Dikiş testi",
            })
        };

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client.SendAsync(request);
    }

    /// <summary>
    /// Kuran kişinin token'ı.
    ///
    /// Device flow tarayıcıda bir insanın onayını gerektiriyor ve burada
    /// otomatikleştirilemez; onun ölçüldüğü yer
    /// <see cref="BridgeDeviceLoginTests"/>. Token AYNI istemciden
    /// (<c>grafirio-client</c>) alınıyor, dolayısıyla claim'leri ve
    /// audience'ı onaydan sonra doğacak token'la aynı — ölçülen dikiş bozulmuyor.
    /// </summary>
    private async Task<string> InstallerTokenAsync(string? companyId)
    {
        var (username, password) = await CreateUserAsync(companyId);

        using var client = new HttpClient();

        var response = await client.PostAsync(
            $"{KeycloakFixture.Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = InstallerClientId,
                ["scope"] = "openid",
                ["username"] = username,
                ["password"] = password,
            }));

        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode,
            $"Kuran kişinin token'ı alınamadı: {body}");

        return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<string> BridgeTokenAsync(BridgeCredentials credentials)
    {
        using var client = new HttpClient();

        var response = await client.PostAsync(credentials.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
            }));

        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode,
            $"Bridge kendi kimliğiyle token alamadı: {body}");

        return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// Realm'de <c>registrationEmailAsUsername</c> açık: Keycloak kullanıcı
    /// adını e-postayla değiştiriyor. Giriş de e-postayla yapılıyor, dönen
    /// değer bu yüzden e-posta.
    /// </summary>
    private async Task<(string Username, string Password)> CreateUserAsync(string? companyId)
    {
        var username = $"kurulumcu-{Guid.NewGuid():N}"[..24];
        var email = $"{username}@grafirio.test";
        const string password = "Kurulum!2026";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var payload = new Dictionary<string, object>
        {
            ["username"] = username,
            ["enabled"] = true,
            ["emailVerified"] = true,
            ["email"] = email,
            ["firstName"] = "Kurulum",
            ["lastName"] = "Testi",
            ["credentials"] = new[]
            {
                new { type = "password", value = password, temporary = false }
            },
        };

        // Şirketsiz kullanıcı bilerek destekleniyor: geçerli bir hesabın kayıt
        // için yetmediğini ölçen test buna dayanıyor.
        if (companyId is not null)
            payload["attributes"] = new Dictionary<string, string[]>
            {
                ["company_id"] = [companyId]
            };

        var response = await client.PostAsJsonAsync(
            $"{KeycloakFixture.BaseUrl}/admin/realms/{KeycloakFixture.Realm}/users", payload);

        Assert.True(response.IsSuccessStatusCode,
            $"Test kullanıcısı açılamadı: {await response.Content.ReadAsStringAsync()}");

        var id = response.Headers.Location?.Segments[^1]
                 ?? throw new InvalidOperationException("Kullanıcı kimliği okunamadı.");

        _users.Add(id);

        return (email, password);
    }

    private async Task<string> AdminTokenAsync()
    {
        using var client = new HttpClient();

        var response = await client.PostAsync(
            $"{KeycloakFixture.BaseUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = keycloak.AdminUsername,
                ["password"] = keycloak.AdminPassword,
            }));

        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Keycloak yöneticisi giriş yapamadı: {body}");

        return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Açılan kullanıcıları ve bridge kimliklerini toplar; realm birikmesin.</summary>
    public async Task DisposeAsync()
    {
        if (!keycloak.Available) return;

        using var client = new HttpClient();

        try
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());
        }
        catch
        {
            return; // temizlik hatası testi etkilemesin
        }

        foreach (var id in _users)
        {
            try
            {
                await client.DeleteAsync(
                    $"{KeycloakFixture.BaseUrl}/admin/realms/{KeycloakFixture.Realm}/users/{id}");
            }
            catch { /* temizlik hatası testi etkilemesin */ }
        }

        var identity = new KeycloakBridgeIdentity(
            new SimpleFactory(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:BaseUrl"] = KeycloakFixture.BaseUrl,
                ["Keycloak:Realm"] = KeycloakFixture.Realm,
                ["Keycloak:AdminUsername"] = keycloak.AdminUsername,
                ["Keycloak:AdminPassword"] = keycloak.AdminPassword,
            }).Build(),
            NullLogger<KeycloakBridgeIdentity>.Instance);

        foreach (var bridgeId in _bridges)
        {
            try { await identity.DisableAsync(bridgeId); }
            catch { /* temizlik hatası testi etkilemesin */ }
        }
    }

    /// <summary>Son görülme bilgisi bu testin konusu değil.</summary>
    private sealed class NoopPresence : IBridgePresence
    {
        public Task TouchAsync(Guid bridgeId, string version, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class SimpleFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new() { Timeout = TimeSpan.FromSeconds(30) };
    }
}

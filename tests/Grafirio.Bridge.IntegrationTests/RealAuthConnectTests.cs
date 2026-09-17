using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Grafirio.Shared.Identity.Extensions;
using Microsoft.AspNetCore.Authentication;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Bir bridge'in buluta bağlanma yolunun TAMAMI, gerçek Keycloak ile.
///
/// Önceki sürümde burada elle yazılmış bir kimlik doğrulama şeması vardı:
/// kendi token'ımız, kendi SHA256 özetimiz, kendi sabit süreli
/// karşılaştırmamız. Yazdığımız şey OAuth Dynamic Client Registration'ın elle
/// yapılmış hâliydi ve auth sunucusu zaten kuruluydu.
///
/// Artık bridge kendi Keycloak client'ı olarak <c>client_credentials</c> ile
/// token alıyor; sunucu o token'ı standart JWT doğrulamasıyla kontrol ediyor.
/// Ölçülen şey: kimliği olan bağlanabiliyor, olmayan bağlanamıyor, iptal
/// edilen dışarıda kalıyor.
///
///     docker compose up -d keycloak
/// </summary>
public class RealAuthConnectTests(KeycloakFixture keycloak)
    : IClassFixture<KeycloakFixture>, IAsyncLifetime
{
    private const string CompanyId = "auth-firma";

    private readonly List<Guid> _created = [];

    private KeycloakBridgeIdentity Identity()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:BaseUrl"] = KeycloakFixture.BaseUrl,
                ["Keycloak:Realm"] = KeycloakFixture.Realm,
                ["Keycloak:AdminUsername"] = keycloak.AdminUsername,
                ["Keycloak:AdminPassword"] = keycloak.AdminPassword,
                ["IdentityOption:Audience"] = KeycloakFixture.Audience,
            })
            .Build();

        return new KeycloakBridgeIdentity(
            new SimpleHttpClientFactory(),
            configuration,
            NullLogger<KeycloakBridgeIdentity>.Instance);
    }

    private async Task<(Guid BridgeId, BridgeCredentials Credentials)> ProvisionAsync()
    {
        var bridgeId = Guid.NewGuid();
        var credentials = await Identity().CreateAsync(bridgeId, CompanyId, "Test");
        _created.Add(bridgeId);
        return (bridgeId, credentials);
    }

    /// <summary>
    /// Gerçek JWT doğrulaması — üretimin taklidi değil, kendisi.
    ///
    /// Önceki sürümde burada elle
    /// <c>AddAuthentication(JwtBearerDefaults.AuthenticationScheme)</c>
    /// çağrılıyordu, yani şema VARSAYILAN oluyordu ve
    /// <c>UseAuthentication()</c> her isteği kendiliğinden doğruluyordu.
    /// Üretimde öyle değil: paylaşılan kurulum <c>AddAuthentication()</c>'ı
    /// varsayılan şema vermeden çağırıp iki şema kaydediyor, dolayısıyla
    /// şemasını söylemeyen bir politika hiçbir isteği doğrulatmıyor.
    ///
    /// Fark testte görünmüyordu: bridge politikası burada çalışıyor, üretimde
    /// her bridge'i token'ı kusursuz olsa bile 401 ile geri çeviriyordu.
    /// Testin kendi kurulumunu yapması, ölçtüğü şeyi ölçülmez yapmıştı.
    /// </summary>
    private static IHost BuildServer(string url) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseUrls(url)
                .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Error))
                .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["IdentityOption:Address"] = KeycloakFixture.Authority,
                        ["IdentityOption:Issuer"] = KeycloakFixture.Authority,
                        ["IdentityOption:Audience"] = KeycloakFixture.Audience,
                    }))
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IBridgeResponseBus, InProcessBridgeResponseBus>();
                    services.AddSingleton<BridgeRegistry>();
                    services.AddSingleton<IBridgePresence, NoopPresence>();
                    services.AddSignalR();

                    // Üretimdeki kimlik doğrulama kurulumu — kendisi.
                    services.AddAuthenticationAndAuthorizationExt(context.Configuration);

                    // Üretimdeki forbid/challenge şeması: reddetmenin hangi
                    // kodu (401 mi 403 mü) döndüğünü bu belirliyor.
                    services.Configure<AuthenticationOptions>(options =>
                    {
                        options.DefaultForbidScheme ??= JwtBearerDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme ??= JwtBearerDefaults.AuthenticationScheme;
                    });

                    services.AddBridgeAuthorization();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapHub<BridgeHub>(BridgeProtocol.HubPath));
                }))
            .Build();

    private static async Task<string> TokenAsync(BridgeCredentials credentials)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        var response = await client.PostAsync(credentials.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
            }));

        response.EnsureSuccessStatusCode();

        var json = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private static HubConnection BuildClient(string url, Func<Task<string?>> token) =>
        new HubConnectionBuilder()
            .WithUrl(url + BridgeProtocol.HubPath, HttpTransportType.WebSockets, http =>
            {
                http.AccessTokenProvider = token;
                http.Headers[BridgeAuthentication.VersionHeader] = BridgeProtocol.Version;
            })
            .Build();

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [SkippableFact]
    public async Task Keycloak_kimligiyle_baglanabiliyor()
    {
        Skip.IfNot(keycloak.Available, keycloak.SkipReason);

        var (bridgeId, credentials) = await ProvisionAsync();
        var url = $"http://127.0.0.1:{FreePort()}";

        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            await using var client = BuildClient(url, async () => await TokenAsync(credentials));
            await client.StartAsync();

            var registry = server.Services.GetRequiredService<BridgeRegistry>();
            await WaitUntil(() => registry.IsOnline(bridgeId), TimeSpan.FromSeconds(15));

            Assert.True(registry.IsOnline(bridgeId));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [SkippableFact]
    public async Task Token_olmadan_baglanilamiyor()
    {
        Skip.IfNot(keycloak.Available, keycloak.SkipReason);

        var url = $"http://127.0.0.1:{FreePort()}";
        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            await using var client = BuildClient(url, () => Task.FromResult<string?>(null));
            await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync());
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// İptal, Keycloak'ta client'ı kapatmak demek. Bu çalışmazsa panelin
    /// "İptal Et" düğmesi yalancı bir güvence olurdu: defterde iptal yazar,
    /// bridge çalışmaya devam ederdi.
    /// </summary>
    [SkippableFact]
    public async Task Iptal_edilen_kimlik_token_alamiyor()
    {
        Skip.IfNot(keycloak.Available, keycloak.SkipReason);

        var (bridgeId, credentials) = await ProvisionAsync();

        // Önce çalıştığını görelim ki testin kendisi yanlış negatif olmasın.
        Assert.NotEmpty(await TokenAsync(credentials));

        await Identity().DisableAsync(bridgeId);

        await Assert.ThrowsAnyAsync<Exception>(() => TokenAsync(credentials));
    }

    /// <summary>
    /// Bridge'ler müşteri sunucularında yaşıyor ve kendiliğinden
    /// güncellenmiyor. Uyumsuz sürümü sessizce kabul etmek, sebebi
    /// anlaşılmayan hatalar üretir.
    /// </summary>
    [SkippableFact]
    public async Task Uyumsuz_protokol_surumu_reddediliyor()
    {
        Skip.IfNot(keycloak.Available, keycloak.SkipReason);

        var (bridgeId, credentials) = await ProvisionAsync();
        var url = $"http://127.0.0.1:{FreePort()}";

        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            await using var client = new HubConnectionBuilder()
                .WithUrl(url + BridgeProtocol.HubPath, HttpTransportType.WebSockets, http =>
                {
                    http.AccessTokenProvider = async () => await TokenAsync(credentials);
                    http.Headers[BridgeAuthentication.VersionHeader] = "999";
                })
                .Build();

            await client.StartAsync();

            var registry = server.Services.GetRequiredService<BridgeRegistry>();

            // Bağlantı kurulsa bile deftere GİRMEMELİ.
            await Task.Delay(500);
            Assert.False(registry.IsOnline(bridgeId));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Açılan Keycloak client'larını kapatır; realm birikmesin.</summary>
    public async Task DisposeAsync()
    {
        if (!keycloak.Available) return;

        foreach (var bridgeId in _created)
        {
            try { await Identity().DisableAsync(bridgeId); }
            catch { /* temizlik hatası testi etkilemesin */ }
        }
    }

    private sealed class NoopPresence : IBridgePresence
    {
        public Task TouchAsync(Guid bridgeId, string version, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new() { Timeout = TimeSpan.FromSeconds(30) };
    }
}

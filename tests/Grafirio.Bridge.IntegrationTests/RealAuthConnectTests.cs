using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Bir bridge'in buluta bağlanma yolunun TAMAMI, taklitsiz.
///
/// Parite testi hub'ı sabit kimlikli sahte bir doğrulayıcıyla kullanıyordu;
/// burada gerçek <see cref="BridgeAuthenticationHandler"/> ve gerçek
/// <see cref="BridgeStore"/> devrede. Ölçülen şey: kayıt sırasında alınan sır
/// gerçekten bağlanmayı sağlıyor mu, sağlamayanlar gerçekten engelleniyor mu.
/// </summary>
public class RealAuthConnectTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private IHost BuildServer(string url) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseUrls(url)
                .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Error))
                .ConfigureServices(services =>
                {
                    services.AddSingleton(mongo.Database);
                    services.AddSingleton<BridgeStore>();
                    services.AddSingleton<IBridgePresence>(
                        sp => sp.GetRequiredService<BridgeStore>());
                    services.AddSingleton<BridgeRegistry>();
                    services.AddSignalR();

                    // Gerçek doğrulayıcı.
                    services.AddAuthentication(BridgeAuthentication.Scheme)
                        .AddScheme<AuthenticationSchemeOptions, BridgeAuthenticationHandler>(
                            BridgeAuthentication.Scheme, _ => { });

                    services.AddAuthorization();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapHub<BridgeHub>(BridgeProtocol.HubPath));
                }))
            .Build();

    private static HubConnection BuildClient(string url, string authorization, string version) =>
        new HubConnectionBuilder()
            .WithUrl(url + BridgeProtocol.HubPath, HttpTransportType.WebSockets, http =>
            {
                http.Headers["Authorization"] = authorization;
                http.Headers[BridgeAuthentication.VersionHeader] = version;
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
    public async Task Kayitli_bridge_baglanabiliyor_ve_cevrimici_gorunuyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = new BridgeStore(mongo.Database, NullLogger<BridgeStore>.Instance);
        var token = await store.CreateEnrollmentTokenAsync("firma-a", "kullanici-1");
        var companyId = await store.RedeemEnrollmentTokenAsync(token);

        Assert.Equal("firma-a", companyId);

        var (bridgeId, secret) = await store.RegisterAsync(
            companyId!, "Merkez", "SRV-01", "1.0.0");

        var url = $"http://127.0.0.1:{FreePort()}";
        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            await using var client = BuildClient(
                url, $"Bridge {bridgeId}:{secret}", BridgeProtocol.Version);

            await client.StartAsync();

            var registry = server.Services.GetRequiredService<BridgeRegistry>();
            await WaitUntil(() => registry.IsOnline(bridgeId), TimeSpan.FromSeconds(10));

            Assert.True(registry.IsOnline(bridgeId));

            // Panel rozetini besleyen "son görüldü" gerçekten yazılmış olmalı.
            //
            // Ayrıca bekleniyor çünkü defter ile Mongo aynı anda güncellenmiyor:
            // hub önce bridge'i deftere alıyor (sorgular hemen gidebilsin diye),
            // kalıcı kaydı sonra yazıyor. Sıra bilinçli — sorgunun başlaması
            // bir yazma işleminin tamamlanmasını beklememeli.
            DateTime? lastSeen = null;
            await WaitUntil(() =>
            {
                lastSeen = store.ListAsync("firma-a").GetAwaiter().GetResult()
                    .Single(b => b.Id == bridgeId).LastSeenAt;
                return lastSeen is not null;
            }, TimeSpan.FromSeconds(10));

            Assert.NotNull(lastSeen);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [SkippableFact]
    public async Task Yanlis_sirla_baglanilamiyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = new BridgeStore(mongo.Database, NullLogger<BridgeStore>.Instance);
        var (bridgeId, _) = await store.RegisterAsync("firma-a", "Merkez", "SRV-01", "1.0.0");

        var url = $"http://127.0.0.1:{FreePort()}";
        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            await using var client = BuildClient(
                url, $"Bridge {bridgeId}:yanlis", BridgeProtocol.Version);

            await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync());

            var registry = server.Services.GetRequiredService<BridgeRegistry>();
            Assert.False(registry.IsOnline(bridgeId));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// Bridge'ler müşteri sunucularında yaşıyor ve kendiliğinden
    /// güncellenmiyor. Uyumsuz sürümü sessizce kabul etmek, sebebi
    /// anlaşılmayan hatalar üretir.
    /// </summary>
    [SkippableFact]
    public async Task Uyumsuz_protokol_surumu_reddediliyor()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);

        var store = new BridgeStore(mongo.Database, NullLogger<BridgeStore>.Instance);
        var (bridgeId, secret) = await store.RegisterAsync("firma-a", "Merkez", "SRV-01", "1.0.0");

        var url = $"http://127.0.0.1:{FreePort()}";
        using var server = BuildServer(url);
        await server.StartAsync();

        try
        {
            await using var client = BuildClient(url, $"Bridge {bridgeId}:{secret}", "999");
            await client.StartAsync();

            var registry = server.Services.GetRequiredService<BridgeRegistry>();

            // Bağlantı kurulsa bile deftere GIRMEMELI.
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
}

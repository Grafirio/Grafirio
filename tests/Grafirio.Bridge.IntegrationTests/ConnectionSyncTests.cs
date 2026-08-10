using Grafirio.Bridge;
using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// <c>BridgeConnectionSync</c>'in DİKİŞİ: Postgres'ten bağlantıyı, Mongo'dan
/// eşlemeyi ve tablo seçimini okuyup, şifreyi çözüp bridge'e gönderiyor mu.
///
/// Bu testin var olma sebebi somut: bu oturumda çıkan hata tam olarak
/// "iki parça ayrı ayrı doğru ama araları bağlanmamış" cinsindendi. Bridge
/// tarafı tanımı işliyordu, sunucu tarafı tanımı hiç göndermiyordu ve ikisi
/// de kendi testinden geçiyordu.
///
/// Burada taklit edilen tek şey kimlik doğrulama; veri yolunun tamamı gerçek.
///
///     docker compose up -d postgres.db.dataanalysis mongo.db sqlserver.db.order
/// </summary>
[Collection("sqlserver")]
public class ConnectionSyncTests(MongoFixture mongo, PostgresFixture postgres, SqlServerFixture sql)
    : IClassFixture<MongoFixture>, IClassFixture<PostgresFixture>, IClassFixture<SqlServerFixture>
{
    private const string CompanyId = "sync-firma";

    private void SkipUnlessReady()
    {
        Skip.IfNot(mongo.Available, mongo.SkipReason);
        Skip.IfNot(postgres.Available, postgres.SkipReason);
        Skip.IfNot(sql.Available, sql.SkipReason);
    }

    /// <summary>Gerçek bir kayıtlı bağlantı yazar; şifresi AES ile şifreli.</summary>
    private async Task<Guid> SeedConnectionAsync()
    {
        await using var db = postgres.CreateContext();

        var connection = new SavedConnection
        {
            Id = Guid.NewGuid(),
            UserId = PostgresFixture.TestUserId,
            CompanyId = CompanyId,
            Name = "Sync Test",
            Host = "127.0.0.1",
            Port = 1433,
            Database = SqlServerFixture.DatabaseName,
            Username = "sa",
            EncryptedPassword = EncryptionHelper.Encrypt(sql.SaPassword),
            TrustServerCertificate = true,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
        };

        db.SavedConnections.Add(connection);
        await db.SaveChangesAsync();

        return connection.Id;
    }

    private sealed record Harness(
        IHost Server, HubConnection Client, BridgeState State, Guid BridgeId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.StopAsync();
            Server.Dispose();
        }
    }

    private async Task<Harness> StartAsync()
    {
        var store = new BridgeStore(mongo.Database, NullLogger<BridgeStore>.Instance);
        var (bridgeId, secret) = await store.RegisterAsync(CompanyId, "Sync", "SRV", "1.0.0");

        var port = FreePort();
        var url = $"http://127.0.0.1:{port}";

        var server = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseUrls(url)
                .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Error))
                .ConfigureServices(services =>
                {
                    services.AddDbContext<DataAnalysisDbContext>(o => o.UseNpgsql(
                        "Host=127.0.0.1;Port=5433;Database=grafirio_dataanalysis;" +
                        "Username=dataanalysis_user;Password=DataAnalysis123!"));

                    services.AddSingleton(mongo.Database);
                    services.AddSingleton<BridgeStore>();
                    services.AddSingleton<ConnectionProfileStore>();
                    services.AddSingleton<IBridgePresence>(s => s.GetRequiredService<BridgeStore>());
                    services.AddSingleton<IBridgeResponseBus, InProcessBridgeResponseBus>();
                    services.AddSingleton<BridgeRegistry>();
                    services.AddSingleton<BridgeConnectionSync>();
                    services.AddSignalR();

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

        await server.StartAsync();
        server.Services.GetRequiredService<BridgeRegistry>().Start();

        var state = new BridgeState(
            NullLogger<BridgeState>.Instance,
            Path.Combine(Path.GetTempPath(), $"sync-state-{Guid.NewGuid():N}.dat"));

        var executor = new QueryExecutor(
            state,
            new QueryAuditLog(NullLogger<QueryAuditLog>.Instance,
                Path.Combine(Path.GetTempPath(), $"sync-audit-{Guid.NewGuid():N}.tsv")),
            NullLogger<QueryExecutor>.Instance);

        var pump = new BridgeQueryPump(executor, state, NullLogger<BridgeQueryPump>.Instance);

        var client = new HubConnectionBuilder()
            .WithUrl(url + BridgeProtocol.HubPath, HttpTransportType.WebSockets, http =>
            {
                http.Headers["Authorization"] = $"Bridge {bridgeId}:{secret}";
                http.Headers[BridgeAuthentication.VersionHeader] = BridgeProtocol.Version;
            })
            .Build();

        pump.Attach(client, CancellationToken.None);
        await client.StartAsync();

        return new Harness(server, client, state, bridgeId);
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [SkippableFact]
    public async Task Bridge_baglanica_bagli_baglantilar_iniyor()
    {
        SkipUnlessReady();

        var connectionId = await SeedConnectionAsync();

        await using var harness = await StartAsync();

        // Eşlemeyi bridge bağlandıktan SONRA değil, önce yazmalıyız ki
        // OnConnectedAsync onu görsün. Bu yüzden bridge yeniden bağlanıyor.
        var store = new BridgeStore(mongo.Database, NullLogger<BridgeStore>.Instance);
        await store.BindConnectionAsync(connectionId, CompanyId, harness.BridgeId);

        await harness.Client.StopAsync();
        await harness.Client.StartAsync();

        await BridgeTestHost.WaitUntil(
            () => harness.State.FindConnection(connectionId) is not null,
            TimeSpan.FromSeconds(15));

        var stored = harness.State.FindConnection(connectionId);

        Assert.NotNull(stored);
        Assert.Equal("Sync Test", stored.Name);
        Assert.Equal("sa", stored.Username);

        // Asıl kanıt: şifre Postgres'te AES ile şifreliydi, doğru çözülüp
        // gönderilmiş olmalı. Yanlış çözülseydi bridge veritabanına
        // bağlanamazdı ve sebebi hiçbir yerde görünmezdi.
        Assert.Equal(sql.SaPassword, stored.Password);
    }

    [SkippableFact]
    public async Task Secili_tablolar_izin_listesi_olarak_iniyor()
    {
        SkipUnlessReady();

        var connectionId = await SeedConnectionAsync();

        var profiles = new ConnectionProfileStore(
            mongo.Database, NullLogger<ConnectionProfileStore>.Instance);

        await profiles.SaveSelectedTablesAsync(
            connectionId, CompanyId, [SqlServerFixture.TableName]);

        await using var harness = await StartAsync();

        var store = new BridgeStore(mongo.Database, NullLogger<BridgeStore>.Instance);
        await store.BindConnectionAsync(connectionId, CompanyId, harness.BridgeId);

        await harness.Client.StopAsync();
        await harness.Client.StartAsync();

        await BridgeTestHost.WaitUntil(
            () => harness.State.FindConnection(connectionId)?.AllowedTables.Count > 0,
            TimeSpan.FromSeconds(15));

        var stored = harness.State.FindConnection(connectionId);

        Assert.NotNull(stored);
        Assert.Equal([SqlServerFixture.TableName], stored.AllowedTables);
    }

    [SkippableFact]
    public async Task Bagli_olmayan_baglanti_gonderilmiyor()
    {
        SkipUnlessReady();

        // Eşlemesi olmayan bir bağlantı bridge'e inmemeli: aksi hâlde
        // müşterinin sunucusunda kullanılmayan şifreler birikirdi.
        var connectionId = await SeedConnectionAsync();

        await using var harness = await StartAsync();

        await harness.Client.StopAsync();
        await harness.Client.StartAsync();
        await Task.Delay(1500);

        Assert.Null(harness.State.FindConnection(connectionId));
    }
}

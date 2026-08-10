using System.Security.Claims;
using System.Text.Encodings.Web;
using Grafirio.Bridge;
using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Gercek bir sunucu ve gercek bir bridge, aralarinda gercek bir WebSocket.
///
/// Taklit edilen tek sey kimlik dogrulama ve Mongo: olculmek istenen sey
/// protokolun kendisi — sorgunun bridge'e gidip satirlarin geri gelmesi ve
/// degerlerin yolda bozulmamasi.
///
/// Sunucu tarafinda <see cref="BridgeHub"/>, <see cref="BridgeRegistry"/> ve
/// <see cref="BridgeDataSourceSession"/>; bridge tarafinda
/// <see cref="QueryExecutor"/> ve <see cref="BridgeQueryPump"/> — hepsi
/// uretimde kosan siniflarin kendisi.
/// </summary>
public sealed class BridgeTestHost : IAsyncDisposable
{
    public static readonly Guid BridgeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public const string CompanyId = "test-company";

    private IHost? _server;
    private HubConnection? _bridgeConnection;

    public string Url { get; private set; } = "";

    public BridgeRegistry Registry { get; private set; } = null!;

    public IHubContext<BridgeHub> HubContext { get; private set; } = null!;

    public BridgeState BridgeState { get; private set; } = null!;

    /// <summary>Sunucuyu ayaga kaldirir ve bridge'i baglar.</summary>
    public async Task StartAsync(BridgeConnection? connection = null)
    {
        var port = FreePort();
        Url = $"http://127.0.0.1:{port}";

        _server = BuildServer(Url);
        await _server.StartAsync();

        Registry = _server.Services.GetRequiredService<BridgeRegistry>();
        // Cevap yolunu bağla — üretimde Program.cs bunu yapıyor. Bağlanmazsa
        // sorgular sessizce zaman aşımına uğrardı.
        Registry.Start();
        HubContext = _server.Services.GetRequiredService<IHubContext<BridgeHub>>();

        // Gercek durum nesnesi: sahte bir tane koymak yerine gecici bir dosya
        // kullaniliyor. Boylece DPAPI ile yaz-oku turu da olculmus oluyor —
        // musteri makinesinde sifreyi saklayan yol tam olarak bu.
        BridgeState = new BridgeState(
            LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
                .CreateLogger<BridgeState>(),
            Path.Combine(Path.GetTempPath(), $"bridge-state-{Guid.NewGuid():N}.dat"));

        // `connection` null verilirse bridge BOŞ başlıyor: bağlantı tanımının
        // buluttan inmesi gerektiğini ölçen testler bunu kullanıyor.
        if (connection is not null)
        {
            BridgeState.UpsertConnection(connection);
            BridgeState.Load();
        }

        _bridgeConnection = await ConnectBridgeAsync(BridgeState);
    }

    /// <summary>
    /// Bağlantı tanımını sunucudan bridge'e gönderir — üretimde
    /// <c>BridgeConnectionSync</c> bunu yapıyor.
    /// </summary>
    public async Task PushConnectionAsync(BridgeConnection connection)
    {
        await HubContext.Clients.Client(Registry.ConnectionIdOf(BridgeId, CompanyId))
            .SendAsync(
                BridgeProtocol.ServerToBridge.ConfigureConnection,
                new ConfigureConnectionRequest(
                    connection.ConnectionId,
                    connection.Name,
                    connection.Host,
                    connection.Port,
                    connection.Database,
                    connection.Username,
                    connection.Password,
                    connection.TrustServerCertificate,
                    connection.AllowedTables));

        // Mesaj tek yönlü; bridge'in yazmasını bekle.
        //
        // Koşul "kayıt var mı" değil "GÖNDERDİĞİMİZ kayıt var mı": güncelleme
        // gönderiminde eski kayıt zaten duruyor ve basit bir varlık kontrolü
        // hemen dönüp yarışı gizliyordu.
        await WaitUntil(
            () => BridgeState.FindConnection(connection.ConnectionId) is { } stored
                  && stored.Name == connection.Name
                  && stored.Password == connection.Password
                  && stored.AllowedTables.Count == connection.AllowedTables.Count,
            TimeSpan.FromSeconds(10));
    }

    private static IHost BuildServer(string url) =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseUrls(url)
                .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
                .ConfigureServices(services =>
                {
                    // Tek replika: cevap yolu süreç içi. Replikalar arası
                    // yönlendirme ayrıca ölçülüyor (BridgeResponseRoutingTests).
                    services.AddSingleton<IBridgeResponseBus, InProcessBridgeResponseBus>();
                    services.AddSingleton<BridgeRegistry>();
                    services.AddSingleton<IBridgePresence, NoopPresence>();
                    services.AddSignalR(o => o.MaximumReceiveMessageSize = 4 * 1024 * 1024);

                    services.AddAuthentication(StubAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, StubAuthHandler>(
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

    private async Task<HubConnection> ConnectBridgeAsync(BridgeState state)
    {
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

        var audit = new QueryAuditLog(
            loggerFactory.CreateLogger<QueryAuditLog>(),
            Path.Combine(Path.GetTempPath(), $"bridge-audit-{Guid.NewGuid():N}.tsv"));

        var executor = new QueryExecutor(state, audit, loggerFactory.CreateLogger<QueryExecutor>());
        var pump = new BridgeQueryPump(
            executor, state, loggerFactory.CreateLogger<BridgeQueryPump>());

        var connection = new HubConnectionBuilder()
            .WithUrl(Url + BridgeProtocol.HubPath, HttpTransportType.WebSockets, http =>
            {
                http.Headers["Authorization"] = $"Bridge {BridgeId}:secret";
                http.Headers[BridgeAuthentication.VersionHeader] = BridgeProtocol.Version;
            })
            .Build();

        pump.Attach(connection, CancellationToken.None);
        await connection.StartAsync();

        // Hub OnConnectedAsync'in defteri doldurmasini bekle: baglantinin
        // kurulmasi ile sunucunun bridge'i taniyor olmasi ayni an degil.
        await WaitUntil(() => Registry.IsOnline(BridgeId), TimeSpan.FromSeconds(10));

        return connection;
    }

    /// <summary>Sunucu tarafindaki oturum — uretimde fabrika bunu kuruyor.</summary>
    public BridgeDataSourceSession OpenBridgeSession(Guid connectionId) =>
        new(BridgeId, connectionId, CompanyId, HubContext, Registry,
            LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
                .CreateLogger<BridgeDataSourceSession>());

    public async Task DisconnectBridgeAsync()
    {
        if (_bridgeConnection is not null) await _bridgeConnection.StopAsync();
        await WaitUntil(() => !Registry.IsOnline(BridgeId), TimeSpan.FromSeconds(10));
    }

    internal static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }

        throw new TimeoutException("Beklenen duruma ulaşılamadı.");
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_bridgeConnection is not null) await _bridgeConnection.DisposeAsync();

        if (_server is not null)
        {
            await _server.StopAsync();
            _server.Dispose();
        }
    }

    /// <summary>Kimlik dogrulama testin konusu degil; sabit bir bridge kimligi veriyor.</summary>
    private sealed class StubAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = BridgeAuthentication.Scheme;

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(BridgeAuthentication.BridgeIdClaim, BridgeId.ToString()),
                    new Claim(BridgeAuthentication.CompanyIdClaim, CompanyId),
                ],
                SchemeName);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class NoopPresence : IBridgePresence
    {
        public Task TouchAsync(Guid bridgeId, string version, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

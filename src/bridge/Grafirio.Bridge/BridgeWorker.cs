using Grafirio.Bridge.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge;

/// <summary>
/// Bridge'in kalbi: buluta giden kanali kurar ve acik tutar.
///
/// Yon burada goruluyor — <b>baglantiyi biz kuruyoruz</b>, bulut bize
/// baglanmiyor. Musterinin firewall'inda hicbir giris portu acilmiyor, yalnizca
/// giden 443 kullaniliyor. Satista soylenen cumlenin koddaki karsiligi bu.
/// </summary>
public class BridgeWorker(
    IOptions<BridgeOptions> options,
    BridgeState state,
    BridgeEnrollment enrollment,
    BridgeQueryPump pump,
    BridgeTokenSource tokens,
    ILogger<BridgeWorker> logger) : BackgroundService
{
    private readonly BridgeOptions _options = options.Value;

    /// <summary>Kalp atisi araligi. Paneldeki "Çevrimiçi" rozetini besliyor.</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        state.Load();

        if (!state.IsEnrolled && !await EnrollWithRetryAsync(stoppingToken))
            return;

        await using var connection = Build();

        using var subscription = pump.Attach(connection, stoppingToken);

        connection.Reconnecting += error =>
        {
            logger.LogWarning(error, "Bulut bağlantısı koptu, yeniden bağlanılıyor.");
            return Task.CompletedTask;
        };

        connection.Reconnected += _ =>
        {
            logger.LogInformation("Bulut bağlantısı yeniden kuruldu.");
            return Task.CompletedTask;
        };

        await ConnectWithRetryAsync(connection, stoppingToken);

        // Kalp atisi. SignalR'in kendi ping'i baglantiyi ayakta tutuyor ama
        // panelin gosterdigi "son goruldu" bilgisini besleyen sey bu.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (connection.State == HubConnectionState.Connected)
                    await connection.InvokeAsync(
                        BridgeProtocol.BridgeToServer.Heartbeat,
                        new BridgeHeartbeat(_options.Version, pump.ActiveQueryCount),
                        stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Kalp atışı gönderilemedi.");
            }

            await Task.Delay(HeartbeatInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Kayit tamamlanana kadar denemeyi surdurur.
    ///
    /// Onceki surumde tek deneme vardi: basarisiz olunca surec ayakta kalir
    /// ama hicbir sey yapmazdi ve kuran kisiden servisi elle yeniden
    /// baslatmasi beklenirdi. Oysa buradaki basarisizliklarin neredeyse hepsi
    /// gecici — kurulum kodunun suresi doldu, kuran kisi henuz onaylamadi,
    /// bulut daha ayakta degil — ve hepsinin cevabi ayni: yeni bir kod alip
    /// tekrar sormak.
    ///
    /// Aralik artiyor: yapilandirmasi hatali bir bridge (yanlis ServerUrl)
    /// aksi halde sonsuza kadar bes saniyede bir kod isterdi.
    /// </summary>
    private async Task<bool> EnrollWithRetryAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(5);

        while (!ct.IsCancellationRequested)
        {
            if (await enrollment.TryEnrollAsync(ct)) return true;

            logger.LogWarning(
                "Kayıt tamamlanamadı. {Seconds} sn sonra yeni bir kurulum kodu " +
                "alınacak; {Path} dosyasındaki ServerUrl ve IdentityUrl " +
                "alanlarının doğru olduğundan emin olun.",
                (int)delay.TotalSeconds, _options.ConfigurationPath);

            await Task.Delay(delay, ct);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 300));
        }

        return false;
    }

    private HubConnection Build()
    {
        var url = _options.ServerUrl.TrimEnd('/') + BridgeProtocol.HubPath;

        logger.LogInformation("Bulut adresi: {Url}", url);

        return new HubConnectionBuilder()
            .WithUrl(url, HttpTransportType.WebSockets, http =>
            {
                // SignalR token'i her baglanti denemesinde yeniden soruyor;
                // yeniden baglanmalarda suresi dolmus bir token kullanilmiyor.
                http.AccessTokenProvider = async () => await tokens.GetAsync();
                http.Headers[BridgeVersionHeader] = BridgeProtocol.Version;
            })
            // Yeniden baglanma araliklari artan: bulut tarafinda bir kesinti
            // varsa yuzlerce bridge'in ayni anda ustune gitmesi isi zorlastirir.
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMinutes(1),
            ])
            .Build();
    }

    private const string BridgeVersionHeader = "X-Grafirio-Bridge-Version";

    /// <summary>
    /// Ilk baglanti. <c>WithAutomaticReconnect</c> yalnizca KURULMUS bir
    /// baglanti koptugunda devreye giriyor; ilk denemenin basarisiz olmasi
    /// servisi olduruyordu.
    /// </summary>
    private async Task ConnectWithRetryAsync(HubConnection connection, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(5);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await connection.StartAsync(ct);
                logger.LogInformation("Buluta bağlanıldı. Bridge: {BridgeId}", state.BridgeId);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Buluta bağlanılamadı, {Seconds} sn sonra yeniden denenecek.",
                    (int)delay.TotalSeconds);

                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 300));
            }
        }
    }

}

public class BridgeOptions
{
    /// <summary>Grafirio bulut adresi, ornegin https://api.grafirio.com</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>
    /// Kimlik sunucusu realm adresi, ornegin
    /// https://login.grafirio.com/realms/grafirio. Kurulum onayi buradan
    /// isteniyor; uc adresleri kesif belgesinden okunuyor.
    /// </summary>
    public string IdentityUrl { get; set; } = "";

    /// <summary>
    /// Kurulum onayinin sorulacagi OAuth istemcisi. Panelin kullandigi public
    /// istemcinin ayni: <c>company_id</c> ve audience mapper'lari orada tanimli
    /// ve ikinci bir istemcide tekrarlanmasi, alanlar ayrisinca sessizce
    /// bozulan bir esleme demek olurdu.
    /// </summary>
    public string InstallerClientId { get; set; } = "grafirio-client";

    /// <summary>Bu bridge'e panelde gorunecek ad.</summary>
    public string? Name { get; set; }

    /// <summary>Yerel durum dosyasi (kimlik ve veritabani bilgileri).</summary>
    public string StatePath { get; set; } = "state.dat";

    /// <summary>Denetim gunlugu.</summary>
    public string AuditLogPath { get; set; } = "audit.tsv";

    /// <summary>Yapilandirma dosyasinin yolu — hata mesajlarinda gosteriliyor.</summary>
    public string ConfigurationPath { get; set; } = "appsettings.json";

    public string Version { get; set; } = "1.0.0";
}

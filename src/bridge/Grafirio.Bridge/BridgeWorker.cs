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
    ILogger<BridgeWorker> logger) : BackgroundService
{
    private readonly BridgeOptions _options = options.Value;

    /// <summary>Kalp atisi araligi. Paneldeki "Çevrimiçi" rozetini besliyor.</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        state.Load();

        if (!state.IsEnrolled)
        {
            if (!await enrollment.TryEnrollAsync(stoppingToken))
            {
                logger.LogError(
                    "Bridge kayıtlı değil ve kayıt yapılamadı. Paneldeki kurulum " +
                    "token'ını {Path} dosyasındaki EnrollmentToken alanına yazıp " +
                    "servisi yeniden başlatın.", _options.ConfigurationPath);
                return;
            }
        }

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

    private HubConnection Build()
    {
        var url = _options.ServerUrl.TrimEnd('/') + BridgeProtocol.HubPath;

        logger.LogInformation("Bulut adresi: {Url}", url);

        return new HubConnectionBuilder()
            .WithUrl(url, HttpTransportType.WebSockets, http =>
            {
                // Sir baslikta gidiyor, sorgu dizesinde degil: sorgu dizesi her
                // ara sunucunun erisim gunlugune yazilir.
                http.Headers["Authorization"] =
                    $"{BridgeAuthenticationScheme} {state.BridgeId}:{state.Secret}";
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

    private const string BridgeAuthenticationScheme = "Bridge";
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

    /// <summary>Panelden alinan tek kullanimlik kayit token'i. Kayit sonrasi silinir.</summary>
    public string? EnrollmentToken { get; set; }

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

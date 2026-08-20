using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Baglanti tanimlarini bridge'e gonderir.
///
/// Bridge yalnizca kendi yerel deposunda tanimli baglantilara sorgu
/// calistiriyor; oraya nasil geldigi bu sinifin isi.
///
/// <b>Kapsam sirket.</b> Bir bridge, sirketinin BUTUN baglantilarini aliyor.
/// Onceden baglanti basina elle yapilan bir eslestirme vardi ve yalnizca
/// eslesenler gonderiliyordu; o kavram kalkti, cunku kullaniciya "bu baglanti
/// hangi makineden okunsun" diye sormanin karsiligi yoktu — masaustu
/// uygulamasini kuran biri zaten veritabanina buluttan ulasilamadigi icin
/// kuruyor.
///
/// Uc tetikleyici var ve ucu de gerekli:
///
///   * Bridge her baglandiginda (<see cref="SyncAllAsync"/>). Sifre ya da
///     tablo secimi arada degismis olabilir.
///   * Yeni bir baglanti kaydedildiginde ya da guncellendiginde
///     (<see cref="SyncOneAsync"/>). Bu olmadan yeni baglanti, bridge bir
///     sonraki yeniden baglanmasina kadar "tanimli degil" ile duserdi.
///   * Baglanti silindiginde (<see cref="ForgetAsync"/>).
///
/// Sifre burada cozulup kanaldan gonderiliyor ve bridge'in diskinde kaliyor.
/// Bulut kalici bir kopya tutmuyor.
/// </summary>
public class BridgeConnectionSync(
    IHubContext<BridgeHub> hub,
    BridgeStore bridges,
    ConnectionProfileStore profiles,
    IServiceScopeFactory scopeFactory,
    ILogger<BridgeConnectionSync> logger)
{
    /// <summary>
    /// Sirketin butun baglantilarini bridge'e yeniden gonderir. Bridge
    /// baglandiginda cagriliyor.
    /// </summary>
    public async Task SyncAllAsync(
        Guid bridgeId, string companyId, string signalRConnectionId, CancellationToken ct = default)
    {
        // Hub'in omru istek disi; DbContext scoped oldugu icin kendi kapsami
        // aciliyor.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();

        var connections = await db.SavedConnections
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .ToListAsync(ct);

        if (connections.Count == 0)
        {
            logger.LogInformation(
                "Bridge {BridgeId} için şirkette kayıtlı bağlantı yok; gönderilecek bir şey de yok.",
                bridgeId);
            return;
        }

        foreach (var connection in connections)
        {
            try
            {
                await SendAsync(signalRConnectionId, connection.Id, companyId, db, ct);
            }
            catch (Exception ex)
            {
                // Bir baglantinin gonderilememesi digerlerini engellememeli;
                // sessiz kalmiyor cunku o baglantiya gelen sorgular
                // "tanimli degil" ile duser.
                logger.LogError(ex,
                    "Bağlantı bridge'e gönderilemedi: {ConnectionId}", connection.Id);
            }
        }

        logger.LogInformation(
            "Bridge {BridgeId} için {Count} bağlantı gönderildi.", bridgeId, connections.Count);
    }

    /// <summary>
    /// Tek bir baglantiyi sirketin cevrimici bridge'ine gonderir. Kayit ve
    /// guncelleme sonrasi cagriliyor.
    /// </summary>
    public async Task SyncOneAsync(
        Guid connectionId, string companyId, BridgeRegistry registry, CancellationToken ct = default)
    {
        if (await OnlineBridgeAsync(companyId, registry, ct) is not { } bridgeId)
        {
            // Cevrimdisi bir bridge'e gonderemeyiz; baglandiginda SyncAllAsync
            // zaten hepsini yollayacak. Sirkette hic bridge yoksa da normal:
            // sorgular buluttan dogrudan gidiyor.
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();

        await SendAsync(
            registry.ConnectionIdOf(bridgeId, companyId), connectionId, companyId, db, ct);
    }

    /// <summary>Baglantiyi bridge'e unutturur. Silme sonrasi cagriliyor.</summary>
    public async Task ForgetAsync(
        Guid connectionId, string companyId, BridgeRegistry registry, CancellationToken ct = default)
    {
        if (await OnlineBridgeAsync(companyId, registry, ct) is not { } bridgeId) return;

        await hub.Clients.Client(registry.ConnectionIdOf(bridgeId, companyId))
            .SendAsync(
                BridgeProtocol.ServerToBridge.RemoveConnection,
                new RemoveConnectionRequest(connectionId), ct);
    }

    /// <summary>
    /// Sirketin cevrimici bridge'i; yoksa <c>null</c>. Yol secimiyle ayni
    /// kural — bkz. <c>DataSourceFactory.ResolveRouteAsync</c>.
    /// </summary>
    private async Task<Guid?> OnlineBridgeAsync(
        string companyId, BridgeRegistry registry, CancellationToken ct)
    {
        foreach (var bridge in await bridges.ListAsync(companyId, ct))
        {
            if (registry.IsOnline(bridge.Id)) return bridge.Id;
        }

        return null;
    }

    private async Task SendAsync(
        string signalRConnectionId, Guid connectionId, string companyId,
        DataAnalysisDbContext db, CancellationToken ct)
    {
        var connection = await db.SavedConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.CompanyId == companyId, ct);

        if (connection is null) return;

        // Secili tablolar izin listesine donusuyor: sistemde zaten "yalnizca
        // secili tablolar islenir" kurali var, bridge tarafinda da ayni kural
        // uygulansin. Secim bossa kisit da yok — aksi halde tablo secmemis bir
        // musteride hicbir sorgu calismazdi.
        var allowedTables = await profiles.GetSelectedTablesAsync(connectionId, companyId, ct);

        await hub.Clients.Client(signalRConnectionId).SendAsync(
            BridgeProtocol.ServerToBridge.ConfigureConnection,
            new ConfigureConnectionRequest(
                connection.Id,
                connection.Name,
                connection.Host,
                connection.Port,
                connection.Database,
                connection.Username,
                EncryptionHelper.Decrypt(connection.EncryptedPassword),
                connection.TrustServerCertificate,
                allowedTables),
            ct);
    }
}

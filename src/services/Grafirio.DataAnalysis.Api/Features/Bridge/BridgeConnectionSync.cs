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
/// calistiriyor; oraya nasil geldigi bu sinifin isi. Iki tetikleyici var ve
/// ikisi de gerekli:
///
///   * Panelden bir baglanti bridge'e baglandiginda.
///   * Bridge her baglandiginda. Bu SART: baglama aninda bridge cevrimdisi
///     olabilir, ayrica sifre ya da izin listesi sonradan degismis olabilir.
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
    /// Bridge'e ait butun baglantilari yeniden gonderir. Bridge baglandiginda
    /// cagriliyor.
    /// </summary>
    public async Task SyncAllAsync(
        Guid bridgeId, string companyId, string signalRConnectionId, CancellationToken ct = default)
    {
        // Hub'in omru istek disi; DbContext scoped oldugu icin kendi kapsamı
        // aciliyor.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();

        var bindings = await bridges.GetBindingsAsync(companyId, ct);
        var connectionIds = bindings.Where(kv => kv.Value == bridgeId).Select(kv => kv.Key).ToList();

        if (connectionIds.Count == 0)
        {
            logger.LogInformation(
                "Bridge {BridgeId} için tanımlı bağlantı yok; gönderilecek bir şey de yok.",
                bridgeId);
            return;
        }

        var connections = await db.SavedConnections
            .Where(c => connectionIds.Contains(c.Id) && c.CompanyId == companyId)
            .ToListAsync(ct);

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

    /// <summary>Tek bir baglantiyi bagli bridge'e gonderir.</summary>
    public async Task SyncOneAsync(
        Guid connectionId, Guid bridgeId, string companyId,
        BridgeRegistry registry, CancellationToken ct = default)
    {
        if (!registry.IsOnline(bridgeId))
        {
            // Cevrimdisi bir bridge'e gonderemeyiz; baglandiginda SyncAllAsync
            // zaten hepsini yollayacak. Sessiz kalmiyoruz ki kullanici
            // "neden hemen calismadi" sorusunun cevabini logda bulabilsin.
            logger.LogInformation(
                "Bridge {BridgeId} çevrimdışı; bağlantı tanımı o bağlandığında gönderilecek.",
                bridgeId);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();

        await SendAsync(
            registry.ConnectionIdOf(bridgeId, companyId), connectionId, companyId, db, ct);
    }

    /// <summary>Baglantiyi bridge'e unutturur.</summary>
    public async Task ForgetAsync(
        Guid connectionId, Guid bridgeId, string companyId,
        BridgeRegistry registry, CancellationToken ct = default)
    {
        if (!registry.IsOnline(bridgeId)) return;

        await hub.Clients.Client(registry.ConnectionIdOf(bridgeId, companyId))
            .SendAsync(
                BridgeProtocol.ServerToBridge.RemoveConnection,
                new RemoveConnectionRequest(connectionId), ct);
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

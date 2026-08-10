using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// Oturum acar. Musteri veritabanina hangi yoldan gidilecegine karar veren yer
/// burasi: baglanti bir bridge'e bagliysa sorgular musterinin agindaki servise
/// gider, degilse buluttan dogrudan TCP acilir. Cagiran taraflarin bu secimden
/// haberi olmuyor.
/// </summary>
public interface IDataSourceFactory
{
    Task<IDataSourceSession> OpenAsync(DataSourceTarget target, CancellationToken ct = default);

    /// <summary>Kayitli baglanti; yol secimi ve sifre cozme isi burada yapilir.</summary>
    Task<IDataSourceSession> OpenAsync(SavedConnection connection, CancellationToken ct = default);

    /// <summary>
    /// "Test Et": baglanilabiliyor mu. Hata firlatmaz — basarisizlik da bir
    /// cevaptir ve kullaniciya sebebiyle birlikte gosterilir.
    /// </summary>
    Task<ProbeResult> ProbeAsync(DataSourceTarget target, CancellationToken ct = default);
}

public sealed record ProbeResult(bool Success, string Message);

public sealed class DataSourceFactory(
    BridgeStore bridges,
    BridgeRegistry registry,
    IHubContext<BridgeHub> hub,
    ILoggerFactory loggerFactory) : IDataSourceFactory
{
    private readonly ILogger<DataSourceFactory> _logger =
        loggerFactory.CreateLogger<DataSourceFactory>();

    public async Task<IDataSourceSession> OpenAsync(
        SavedConnection connection, CancellationToken ct = default)
    {
        var bridgeId = await bridges.GetBoundBridgeAsync(connection.Id, ct);

        if (bridgeId is null)
            return await OpenAsync(DataSourceTarget.From(connection), ct);

        _logger.LogDebug(
            "Bağlantı {ConnectionId} bridge {BridgeId} üzerinden okunacak.",
            connection.Id, bridgeId);

        return new BridgeDataSourceSession(
            bridgeId.Value, connection.Id, connection.CompanyId, hub, registry,
            loggerFactory.CreateLogger<BridgeDataSourceSession>());
    }

    public async Task<IDataSourceSession> OpenAsync(
        DataSourceTarget target, CancellationToken ct = default)
    {
        try
        {
            return await DirectDataSourceSession.OpenAsync(
                target, DataSourceTarget.DefaultConnectTimeoutSeconds, ct);
        }
        catch (SqlException ex)
        {
            throw new DataSourceException(
                $"Bağlantı kurulamadı ({target}): {ex.Message} (SQL hata no: {ex.Number})", ex);
        }
    }

    public async Task<ProbeResult> ProbeAsync(
        DataSourceTarget target, CancellationToken ct = default)
    {
        // Kullanici adi ve sifre loglanmiyor; host/veritabani teshis icin gerekli.
        _logger.LogInformation("Bağlantı deneniyor: {Target}", target);

        try
        {
            await using var session = await DirectDataSourceSession.OpenAsync(
                target, DataSourceTarget.ProbeConnectTimeoutSeconds, ct);

            return new ProbeResult(true, "Bağlantı başarılı");
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "SQL bağlantı hatası. Numara: {Number}", ex.Number);
            return new ProbeResult(false, $"SQL hatası: {ex.Message} (hata no: {ex.Number})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bağlantı kurulamadı");
            return new ProbeResult(false, $"Bağlantı kurulamadı: {ex.Message}");
        }
    }
}

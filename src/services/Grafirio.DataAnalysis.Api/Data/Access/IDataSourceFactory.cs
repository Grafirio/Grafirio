using Grafirio.DataAnalysis.Api.Data.Entities;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// Oturum acar. Musteri veritabanina hangi yoldan gidilecegine karar veren yer
/// burasi olacak: bugun tek yol var (dogrudan baglanti), bridge geldiginde
/// kayitli baglantinin moduna bakip ikinci yolu secebilecek. Cagiran taraflarin
/// bu secimden haberi olmuyor.
/// </summary>
public interface IDataSourceFactory
{
    Task<IDataSourceSession> OpenAsync(DataSourceTarget target, CancellationToken ct = default);

    /// <summary>Kayitli baglanti; sifre cozme isi burada yapilir.</summary>
    Task<IDataSourceSession> OpenAsync(SavedConnection connection, CancellationToken ct = default);

    /// <summary>
    /// "Test Et": baglanilabiliyor mu. Hata firlatmaz — basarisizlik da bir
    /// cevaptir ve kullaniciya sebebiyle birlikte gosterilir.
    /// </summary>
    Task<ProbeResult> ProbeAsync(DataSourceTarget target, CancellationToken ct = default);
}

public sealed record ProbeResult(bool Success, string Message);

public sealed class DirectDataSourceFactory(ILogger<DirectDataSourceFactory> logger) : IDataSourceFactory
{
    public Task<IDataSourceSession> OpenAsync(SavedConnection connection, CancellationToken ct = default) =>
        OpenAsync(DataSourceTarget.From(connection), ct);

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
        logger.LogInformation("Bağlantı deneniyor: {Target}", target);

        try
        {
            await using var session = await DirectDataSourceSession.OpenAsync(
                target, DataSourceTarget.ProbeConnectTimeoutSeconds, ct);

            return new ProbeResult(true, "Bağlantı başarılı");
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "SQL bağlantı hatası. Numara: {Number}", ex.Number);
            return new ProbeResult(false, $"SQL hatası: {ex.Message} (hata no: {ex.Number})");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bağlantı kurulamadı");
            return new ProbeResult(false, $"Bağlantı kurulamadı: {ex.Message}");
        }
    }
}

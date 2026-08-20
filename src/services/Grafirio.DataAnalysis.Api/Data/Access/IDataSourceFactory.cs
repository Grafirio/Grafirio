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
    ///
    /// Kayitli baglanti aliyor, ham kimlik bilgisi DEGIL. Onceden
    /// <see cref="DataSourceTarget"/> aliyordu ve bu, testin bridge'i hic
    /// kullanamamasi demekti: yol secimi baglantinin bridge esleşmesine bagli,
    /// esleşme de baglantinin kimligine. Sonucu somuttu — firewall arkasindaki
    /// bir veritabani icin "Test Et" HER ZAMAN basarisiz oluyordu, bridge
    /// kurulu ve cevrimici olsa bile; arayuz kaydi teste bagladigi icin o
    /// baglanti hic kaydedilemiyordu.
    /// </summary>
    Task<ProbeResult> ProbeAsync(SavedConnection connection, CancellationToken ct = default);
}

public sealed record ProbeResult(bool Success, string Message);

/// <summary>
/// Bir sirketin sorgularinin gidecegi yol.
///
/// <paramref name="HasOfflineBridge"/> ayri tasiniyor cunku "bridge yok" ile
/// "bridge var ama kapali" ayni sey degil: ikincisinde dogrudan denemek yine
/// dogru, ama basarisiz olursa kullaniciya soylenecek sey farkli.
/// </summary>
internal sealed record BridgeRoute(Guid? OnlineBridgeId, bool HasOfflineBridge);

public sealed class DataSourceFactory(
    BridgeStore bridges,
    BridgeRegistry registry,
    IHubContext<BridgeHub> hub,
    ILoggerFactory loggerFactory) : IDataSourceFactory
{
    private readonly ILogger<DataSourceFactory> _logger =
        loggerFactory.CreateLogger<DataSourceFactory>();

    /// <summary>
    /// Bir sirketin sorgularinin hangi yoldan gidecegi.
    ///
    /// Kural tek cumle: <b>sirketin cevrimici bir bridge'i varsa oradan
    /// okunur.</b> Kullanici hicbir sey secmiyor.
    ///
    /// Onceden yol, baglanti basina elle yapilan bir "eslestirme" kaydindan
    /// okunuyordu. Bu, kullaniciya sorulacak bir soru degil: masaustu
    /// uygulamasini kuran biri, zaten veritabanina buradan ulasilamadigi icin
    /// kuruyor. Eslestirme yapilmadiginda ise sorgu sessizce buluttan
    /// dogrudan denenip zaman asimina ugruyordu — yani kurulum yapilmis ama
    /// hicbir ise yaramamis oluyordu.
    /// </summary>
    private async Task<BridgeRoute> ResolveRouteAsync(
        SavedConnection connection, CancellationToken ct)
    {
        var registered = await bridges.ListAsync(connection.CompanyId, ct);

        if (registered.Count == 0) return new BridgeRoute(null, false);

        // Birden fazla bridge kurulu olabilir (yedek makine, ayri sube).
        // Hangisinden okundugu fark etmiyor: hepsi ayni sirketin agindaki
        // makineler ve ayni baglanti tanimlarini aliyor.
        foreach (var bridge in registered)
        {
            if (registry.IsOnline(bridge.Id)) return new BridgeRoute(bridge.Id, false);
        }

        return new BridgeRoute(null, HasOfflineBridge: true);
    }

    public async Task<IDataSourceSession> OpenAsync(
        SavedConnection connection, CancellationToken ct = default)
    {
        var route = await ResolveRouteAsync(connection, ct);

        if (route.OnlineBridgeId is { } bridgeId)
        {
            _logger.LogDebug(
                "Bağlantı {ConnectionId} bridge {BridgeId} üzerinden okunacak.",
                connection.Id, bridgeId);

            return new BridgeDataSourceSession(
                bridgeId, connection.Id, connection.CompanyId, hub, registry,
                loggerFactory.CreateLogger<BridgeDataSourceSession>());
        }

        // Bridge kayitli ama cevrimdisi: yine de buluttan deneniyor. Musterinin
        // veritabani buluttan erisilebilir olabilir ve o senaryoyu, masaustu
        // uygulamasi kapali diye durdurmanin anlami yok. Erisilemiyorsa hata
        // mesaji sebebi soyluyor; aksi halde kullanici sonu gelmeyen bir zaman
        // asimiyla bas basa kaliyordu.
        if (route.HasOfflineBridge)
        {
            _logger.LogInformation(
                "Şirket {CompanyId} için kayıtlı bridge çevrimdışı; " +
                "bağlantı {ConnectionId} buluttan doğrudan deneniyor.",
                connection.CompanyId, connection.Id);
        }

        try
        {
            return await OpenAsync(DataSourceTarget.From(connection), ct);
        }
        catch (DataSourceException ex) when (route.HasOfflineBridge)
        {
            throw new DataSourceException(
                $"{ex.Message} — Masaüstü uygulamanız çevrimdışı olduğu için " +
                "bağlantı buluttan denendi. Uygulamayı çalıştırıp tekrar deneyin.", ex);
        }
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
        SavedConnection connection, CancellationToken ct = default)
    {
        var route = await ResolveRouteAsync(connection, ct);
        var routeName = route.OnlineBridgeId is { } id ? $"bridge {id}" : "doğrudan";

        // Kullanici adi ve sifre loglanmiyor; host/veritabani teshis icin gerekli.
        _logger.LogInformation(
            "Bağlantı deneniyor ({Route}): {Target}",
            routeName, DataSourceTarget.From(connection));

        // Dogrudan yolda baglantiyi acabilmek yeterli bir kanit; bridge
        // yolunda degil. Orada kanal ayakta olsa bile bridge veritabanina
        // ulasamiyor ya da baglantiyi henuz tanimiyor olabilir, ve bunlarin
        // ikisi de ancak gercek bir sorgu gonderilince ortaya cikar. Tek ve
        // ayni kanit ikisinde de kullaniliyor: calisan bir SELECT.
        // Cevrimdisi bir bridge varken de dogrudan deneniyor (bkz. OpenAsync);
        // basarisiz olursa sebep mesaja ekleniyor, cunku "baglanilamadi" tek
        // basina masaustu uygulamasinin kapali oldugunu dusundurmuyor.
        var offlineHint = route.HasOfflineBridge
            ? " — Masaüstü uygulamanız çevrimdışı olduğu için bağlantı buluttan " +
              "denendi. Uygulamayı çalıştırıp tekrar deneyin."
            : string.Empty;

        try
        {
            await using var session = await OpenProbeSessionAsync(
                connection, route.OnlineBridgeId, ct);

            await session.ScalarAsync<int>(
                "SELECT 1", timeoutSeconds: DataSourceTarget.ProbeConnectTimeoutSeconds, ct: ct);

            return new ProbeResult(true, route.OnlineBridgeId is null
                ? "Bağlantı başarılı"
                : "Bağlantı başarılı (masaüstü uygulaması üzerinden)");
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "SQL bağlantı hatası. Numara: {Number}", ex.Number);
            return new ProbeResult(false,
                $"SQL hatası: {ex.Message} (hata no: {ex.Number}){offlineHint}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bağlantı kurulamadı ({Route})", routeName);
            return new ProbeResult(false, $"Bağlantı kurulamadı: {ex.Message}{offlineHint}");
        }
    }

    /// <summary>
    /// Test icin oturum. <see cref="OpenAsync(SavedConnection, CancellationToken)"/>
    /// ile ayni yol secimini yapiyor; tek fark dogrudan yolda kullanilan
    /// zaman asimi. Kullanici ekranin basinda bekliyor, yanlis yazilmis bir
    /// host icin yarim dakika beklemesinin anlami yok.
    /// </summary>
    private async Task<IDataSourceSession> OpenProbeSessionAsync(
        SavedConnection connection, Guid? bridgeId, CancellationToken ct)
    {
        if (bridgeId is null)
        {
            return await DirectDataSourceSession.OpenAsync(
                DataSourceTarget.From(connection),
                DataSourceTarget.ProbeConnectTimeoutSeconds, ct);
        }

        return new BridgeDataSourceSession(
            bridgeId.Value, connection.Id, connection.CompanyId, hub, registry,
            loggerFactory.CreateLogger<BridgeDataSourceSession>());
    }
}

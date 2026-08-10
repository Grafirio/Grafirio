using Grafirio.Bridge;
using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Data.Access;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Bağlantı tanımının buluttan bridge'e inmesi.
///
/// Bu, gerçek bridge ikilisini bir süreç olarak çalıştırınca ortaya çıkan
/// boşluktu: bridge kayıt oluyor, bağlanıyor, kalp atışı gönderiyor — ve her
/// sorguyu "bu bağlantı tanımlı değil" diye reddediyordu, çünkü yerel
/// deposuna bir şey yazan hiçbir yol yoktu.
///
/// Diğer testler bu boşluğu göremiyordu: <c>UpsertConnection</c>'ı doğrudan
/// çağırıp bridge'i hazır durumda başlatıyorlardı. Buradaki testler bridge'i
/// BOŞ başlatıyor.
/// </summary>
[Collection("sqlserver")]
public class ConnectionPushTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private static readonly Guid ConnectionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private BridgeConnection Connection(params string[] allowedTables) => new()
    {
        ConnectionId = ConnectionId,
        Name = "e2e",
        Host = "127.0.0.1",
        Port = 1433,
        Database = SqlServerFixture.DatabaseName,
        Username = "sa",
        Password = sql.SaPassword,
        TrustServerCertificate = true,
        AllowedTables = allowedTables.ToList(),
    };

    [SkippableFact]
    public async Task Tanim_inmeden_sorgu_reddediliyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync();   // bridge boş

        await using var session = host.OpenBridgeSession(ConnectionId);

        var ex = await Assert.ThrowsAsync<DataSourceException>(() =>
            session.QueryRowsAsync($"SELECT * FROM {SqlServerFixture.TableName}"));

        Assert.Contains(QueryFailure.UnknownConnection, ex.Message);
    }

    [SkippableFact]
    public async Task Tanim_indikten_sonra_sorgu_calisiyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync();

        await host.PushConnectionAsync(Connection());

        await using var session = host.OpenBridgeSession(ConnectionId);
        var rows = await session.QueryRowsAsync(
            $"SELECT * FROM {SqlServerFixture.TableName} ORDER BY Id");

        Assert.NotEmpty(rows);
    }

    /// <summary>
    /// Şifre buluttan iniyor ve bridge'in diskinde DPAPI ile şifreli kalıyor.
    /// Düz metin olarak dursaydı, müşteriye verilen "şifreniz sizde durur"
    /// sözü yarım kalırdı — orada durur ama korunmasız.
    /// </summary>
    [SkippableFact]
    public async Task Inen_sifre_diskte_duz_metin_degil()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync();
        await host.PushConnectionAsync(Connection());

        var bytes = await File.ReadAllBytesAsync(host.BridgeState.FilePath);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain(sql.SaPassword, text);
    }

    /// <summary>
    /// İzin listesi de tanımla birlikte iniyor. İnmezse en muhafazakâr
    /// müşterinin istediği kemer sessizce devre dışı kalırdı.
    /// </summary>
    [SkippableFact]
    public async Task Izin_listesi_de_iniyor_ve_uygulaniyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync();
        await host.PushConnectionAsync(Connection(SqlServerFixture.TableName));

        await using var session = host.OpenBridgeSession(ConnectionId);

        // İzinli tablo geçiyor.
        Assert.NotEmpty(await session.QueryRowsAsync(
            $"SELECT * FROM {SqlServerFixture.TableName}"));

        // Listede olmayan tablo reddediliyor — sorgu veritabanına hiç gitmiyor.
        var ex = await Assert.ThrowsAsync<DataSourceException>(() =>
            session.QueryRowsAsync("SELECT * FROM dbo.BaskaBirTablo"));

        Assert.Contains(QueryFailure.TableNotAllowed, ex.Message);
    }

    /// <summary>
    /// Yeniden gönderim mevcut tanımın üzerine yazmalı: şifre değiştiğinde
    /// bridge eskisini kullanmaya devam ederse bağlantı sessizce bozulur.
    /// </summary>
    [SkippableFact]
    public async Task Tekrar_gonderim_tanimi_guncelliyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync();

        await host.PushConnectionAsync(Connection());
        Assert.Empty(host.BridgeState.FindConnection(ConnectionId)!.AllowedTables);

        await host.PushConnectionAsync(Connection(SqlServerFixture.TableName));

        var stored = host.BridgeState.FindConnection(ConnectionId)!;
        Assert.Single(stored.AllowedTables);
        Assert.Single(host.BridgeState.Connections);
    }
}

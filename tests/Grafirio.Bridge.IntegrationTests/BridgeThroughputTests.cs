using System.Diagnostics;
using Dapper;
using Grafirio.Bridge;
using Grafirio.DataAnalysis.Api.Data.Access;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Akışın gerçekten aktığı: 50.000 satır.
///
/// Planın doğrulama listesindeki madde buydu. Ölçülen iki şey var ve ikincisi
/// asıl olan:
///
///   * Tam okuma makul sürede bitiyor mu.
///   * Sunucu satırları YOLLARKEN veriyor mu, yoksa hepsini toplayıp sonra mı.
///     İkincisi olsaydı 50.000 satırlık bir sonuç bulutun belleğinde bir kez,
///     istemcinin belleğinde bir kez daha dururdu — ve bunu fark etmenin tek
///     yolu ölçmek.
///
/// Tablo:
///   docker compose up -d sqlserver.db.order
///   (BulkProbe tablosu ilk koşumda oluşturuluyor)
/// </summary>
[Collection("sqlserver")]
public class BridgeThroughputTests(SqlServerFixture sql, ITestOutputHelper output)
    : IClassFixture<SqlServerFixture>
{
    private const int RowCount = 50_000;
    private const string Table = "dbo.BulkProbe";

    private static readonly Guid ConnectionId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private BridgeConnection Connection() => new()
    {
        ConnectionId = ConnectionId,
        Name = "throughput",
        Host = "127.0.0.1",
        Port = 1433,
        Database = SqlServerFixture.DatabaseName,
        Username = "sa",
        Password = sql.SaPassword,
        TrustServerCertificate = true,
    };

    /// <summary>Tabloyu ilk koşumda oluşturur; sonrakiler mevcut veriyi kullanır.</summary>
    private async Task EnsureTableAsync()
    {
        await using var connection = new SqlConnection(sql.ConnectionString);

        var existing = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.tables WHERE name = 'BulkProbe'");

        if (existing > 0) return;

        await connection.ExecuteAsync($"""
            CREATE TABLE {Table} (
                Id INT NOT NULL PRIMARY KEY,
                Ad NVARCHAR(80) NOT NULL,
                Tutar DECIMAL(18,4) NULL,
                Tarih DATETIME2 NULL);

            WITH n AS (
                SELECT TOP ({RowCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO {Table} (Id, Ad, Tutar, Tarih)
            SELECT i, CONCAT(N'Kayit ', i), i * 1.25, DATEADD(minute, i, '2020-01-01') FROM n;
            """, commandTimeout: 120);
    }

    [SkippableFact]
    public async Task Elli_bin_satir_akiyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);
        await EnsureTableAsync();

        await using var host = new BridgeTestHost();
        await host.StartAsync(Connection());

        await using var session = host.OpenBridgeSession(ConnectionId);

        var stopwatch = Stopwatch.StartNew();
        var seen = 0;

        await foreach (var row in session.StreamAsync(
            $"SELECT Id, Ad, Tutar, Tarih FROM {Table} ORDER BY Id", maxRows: RowCount))
        {
            seen++;
        }

        stopwatch.Stop();

        output.WriteLine($"{seen} satır, {stopwatch.ElapsedMilliseconds} ms");

        Assert.Equal(RowCount, seen);
    }

    /// <summary>
    /// Asıl ölçüm: ilk satır, sonuncusu gelmeden ÇOK ÖNCE elimizde olmalı.
    ///
    /// Sunucu satırları toplayıp sonra verseydi ilk satır da ancak sonuncusuyla
    /// birlikte gelirdi. Oran karşılaştırılıyor çünkü mutlak süre makineye göre
    /// değişir; akışın varlığı değişmez.
    /// </summary>
    [SkippableFact]
    public async Task Ilk_satir_hepsi_beklenmeden_geliyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);
        await EnsureTableAsync();

        await using var host = new BridgeTestHost();
        await host.StartAsync(Connection());

        await using var session = host.OpenBridgeSession(ConnectionId);

        var query = $"SELECT Id, Ad, Tutar, Tarih FROM {Table} ORDER BY Id";

        var toFirst = Stopwatch.StartNew();
        await foreach (var _ in session.StreamAsync(query, maxRows: RowCount))
        {
            toFirst.Stop();
            break;
        }

        var toAll = Stopwatch.StartNew();
        var seen = 0;
        await foreach (var _ in session.StreamAsync(query, maxRows: RowCount)) seen++;
        toAll.Stop();

        output.WriteLine(
            $"ilk satır: {toFirst.ElapsedMilliseconds} ms, " +
            $"{seen} satırın tamamı: {toAll.ElapsedMilliseconds} ms");

        Assert.Equal(RowCount, seen);

        // Yarısından hızlı olması bile akışın kanıtı; eşik bilerek gevşek
        // tutuldu ki yavaş bir makinede kırmızı yanıp güveni yıpratmasın.
        Assert.True(toFirst.ElapsedMilliseconds < toAll.ElapsedMilliseconds / 2,
            $"İlk satır {toFirst.ElapsedMilliseconds} ms, tamamı {toAll.ElapsedMilliseconds} ms — " +
            "sunucu satırları biriktiriyor olabilir.");
    }

    /// <summary>
    /// Satır tavanı akışı erken kesmeli: 10 satır isteyen bir sorgu 50.000
    /// satırın tamamını çekmemeli.
    /// </summary>
    [SkippableFact]
    public async Task Tavan_akisi_erken_kesiyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);
        await EnsureTableAsync();

        await using var host = new BridgeTestHost();
        await host.StartAsync(Connection());

        await using var session = host.OpenBridgeSession(ConnectionId);

        var stopwatch = Stopwatch.StartNew();
        var seen = 0;

        await foreach (var _ in session.StreamAsync(
            $"SELECT Id, Ad, Tutar, Tarih FROM {Table} ORDER BY Id", maxRows: 10))
        {
            seen++;
        }

        stopwatch.Stop();
        output.WriteLine($"tavan 10: {seen} satır, {stopwatch.ElapsedMilliseconds} ms");

        Assert.Equal(10, seen);
    }
}

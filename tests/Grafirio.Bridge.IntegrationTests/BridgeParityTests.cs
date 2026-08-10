using Dapper;
using Grafirio.Bridge;
using Grafirio.DataAnalysis.Api.Data.Access;
using Microsoft.Data.SqlClient;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// <b>Asıl güvence:</b> aynı sorgu, bir kez buluttan doğrudan bir kez müşteri
/// ağındaki bridge üzerinden çalıştırıldığında birebir aynı sonucu vermeli.
///
/// Faz 1'de veriye giden yol tek kapıya indirildi ve çağrı noktalarının iki
/// yolu ayırt etmeyeceği söylendi. Bu testin ölçtüğü şey o söz. Birim testleri
/// parçaları ayrı ayrı doğruluyor; buradaki, ikisinin gerçekten aynı şeyi
/// döndürdüğü.
///
/// Özellikle tip korunması ölçülüyor: her şey metne düşseydi şema profilindeki
/// min/max karşılaştırması metin sırasına düşerdi ve kimse fark etmezdi.
///
/// SQL Server konteyneri gerekiyor:
///     docker compose up -d sqlserver.db.order
/// Konteyner yoksa testler atlanıyor — kırmızı bir test, çalıştırılamamış bir
/// testle aynı şey değil.
/// </summary>
[Collection("sqlserver")]
public class BridgeParityTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private static readonly Guid ConnectionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private BridgeConnection LocalConnection() => new()
    {
        ConnectionId = ConnectionId,
        Name = "test",
        Host = "127.0.0.1",
        Port = 1433,
        Database = SqlServerFixture.DatabaseName,
        Username = "sa",
        Password = sql.SaPassword,
        TrustServerCertificate = true,
    };

    /// <summary>
    /// Karşılaştırma değerin KENDİSİ ve TİPİ üzerinden yapılıyor. Yalnızca
    /// metin hâline bakılsaydı 100 ile "100" arasındaki fark görünmezdi — ki
    /// bu testin yakalamak istediği hata tam olarak odur.
    /// </summary>
    private static void AssertSameRows(
        IReadOnlyList<QueryRow> direct, IReadOnlyList<QueryRow> bridge)
    {
        Assert.Equal(direct.Count, bridge.Count);

        for (var i = 0; i < direct.Count; i++)
        {
            var expected = direct[i].Values;
            var actual = bridge[i].Values;

            Assert.Equal(
                expected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
                actual.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

            foreach (var (column, expectedValue) in expected)
            {
                var actualValue = actual[column];

                if (expectedValue is null)
                {
                    Assert.Null(actualValue);
                    continue;
                }

                Assert.NotNull(actualValue);

                // Tam sayılar SqlValueCodec'ten long, ondalıklar decimal olarak
                // çıkıyor; SqlClient int/decimal veriyor. Sayısal DEĞERİN aynı
                // olması aranıyor, CLR tipinin birebir aynı olması değil —
                // ama ikisi de sayı olmalı, biri metne düşmemeli.
                if (IsNumeric(expectedValue) || IsNumeric(actualValue))
                {
                    Assert.True(IsNumeric(expectedValue) && IsNumeric(actualValue),
                        $"'{column}': biri sayı diğeri değil " +
                        $"({expectedValue.GetType().Name} / {actualValue.GetType().Name})");

                    Assert.Equal(
                        Convert.ToDecimal(expectedValue),
                        Convert.ToDecimal(actualValue));
                    continue;
                }

                Assert.Equal(expectedValue, actualValue);
            }
        }
    }

    private static bool IsNumeric(object value) =>
        value is byte or short or int or long or float or double or decimal;

    private async Task<(IReadOnlyList<QueryRow> Direct, IReadOnlyList<QueryRow> Bridge)>
        RunBothAsync(string query)
    {
        await using var host = new BridgeTestHost();
        await host.StartAsync(LocalConnection());

        var target = new DataSourceTarget(
            "127.0.0.1", 1433, SqlServerFixture.DatabaseName, "sa", sql.SaPassword, true);

        await using var directSession = await DirectDataSourceSession.OpenAsync(
            target, 30, CancellationToken.None);
        var direct = await directSession.QueryRowsAsync(query);

        await using var bridgeSession = host.OpenBridgeSession(ConnectionId);
        var bridge = await bridgeSession.QueryRowsAsync(query);

        return (direct, bridge);
    }

    [SkippableFact]
    public async Task Karisik_tipli_tablo_iki_yolda_da_ayni_geliyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        var (direct, bridge) = await RunBothAsync(
            $"SELECT * FROM {SqlServerFixture.TableName} ORDER BY Id");

        Assert.NotEmpty(direct);
        AssertSameRows(direct, bridge);
    }

    [SkippableFact]
    public async Task Sayilar_metne_dusmuyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync(LocalConnection());

        await using var session = host.OpenBridgeSession(ConnectionId);
        var rows = await session.QueryRowsAsync(
            $"SELECT Id, Tutar FROM {SqlServerFixture.TableName} ORDER BY Id");

        // Metin sırasında "100" < "99" olur. Bridge yolunda bu tuzağa
        // düşülmediğini ölçen doğrudan kontrol.
        var ids = rows.Select(r => Convert.ToInt64(r["Id"])).ToList();
        Assert.Equal(ids.OrderBy(x => x), ids);

        Assert.All(rows, row => Assert.True(IsNumeric(row["Tutar"]!),
            $"Tutar metne düştü: {row["Tutar"]?.GetType().Name}"));
    }

    [SkippableFact]
    public async Task Parametreli_sorgu_iki_yolda_da_ayni_geliyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync(LocalConnection());

        const string query =
            "SELECT Id, Ad FROM " + SqlServerFixture.TableName +
            " WHERE Ulke = @ulke ORDER BY Id";

        var target = new DataSourceTarget(
            "127.0.0.1", 1433, SqlServerFixture.DatabaseName, "sa", sql.SaPassword, true);

        await using var directSession = await DirectDataSourceSession.OpenAsync(
            target, 30, CancellationToken.None);
        var direct = await directSession.QueryRowsAsync(query, new { ulke = "TR" });

        await using var bridgeSession = host.OpenBridgeSession(ConnectionId);
        var bridge = await bridgeSession.QueryRowsAsync(query, new { ulke = "TR" });

        Assert.NotEmpty(direct);
        AssertSameRows(direct, bridge);
    }

    /// <summary>
    /// İlişki keşfi <c>IN @Names</c> yazıp Dapper'ın listeyi tek tek
    /// parametrelere açmasına güveniyor. Bridge yolunda liste desteklenmezse
    /// o sorgu sessizce boş döner — yani hiçbir ilişki bulunamaz ve sebebi
    /// hiçbir yerde görünmez.
    /// </summary>
    [SkippableFact]
    public async Task Liste_parametresi_bridge_yolunda_da_calisiyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync(LocalConnection());

        await using var session = host.OpenBridgeSession(ConnectionId);
        var rows = await session.QueryRowsAsync(
            $"SELECT Id, Ulke FROM {SqlServerFixture.TableName} WHERE Ulke IN @ulkeler ORDER BY Id",
            new { ulkeler = new[] { "TR", "DE" } });

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Contains(row.GetString("Ulke"), new[] { "TR", "DE" }));
    }

    [SkippableFact]
    public async Task Satir_tavani_uygulaniyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync(LocalConnection());

        await using var session = host.OpenBridgeSession(ConnectionId);

        var rows = new List<QueryRow>();
        await foreach (var row in session.StreamAsync(
            $"SELECT * FROM {SqlServerFixture.TableName} ORDER BY Id", maxRows: 2))
        {
            rows.Add(row);
        }

        Assert.Equal(2, rows.Count);
    }

    [SkippableFact]
    public async Task Yazma_sorgusu_bridge_tarafinda_reddediliyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync(LocalConnection());

        await using var session = host.OpenBridgeSession(ConnectionId);

        var ex = await Assert.ThrowsAsync<DataSourceException>(() =>
            session.QueryRowsAsync($"DROP TABLE {SqlServerFixture.TableName}"));

        Assert.Contains("okuma", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Asıl kanıt: tablo hâlâ duruyor.
        await using var check = new SqlConnection(sql.ConnectionString);
        var exists = await check.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.tables WHERE name = '{SqlServerFixture.BareTableName}'");

        Assert.Equal(1, exists);
    }

    /// <summary>
    /// Bridge çevrimdışıyken sorgu sonsuza kadar beklememeli. Teşhis
    /// edilemeyen bir askıda kalma, görülebilen bir hatadan çok daha kötüdür.
    /// </summary>
    [SkippableFact]
    public async Task Bridge_cevrimdisiyken_acik_hata_donuyor()
    {
        Skip.IfNot(sql.Available, sql.SkipReason);

        await using var host = new BridgeTestHost();
        await host.StartAsync(LocalConnection());
        await host.DisconnectBridgeAsync();

        await using var session = host.OpenBridgeSession(ConnectionId);

        var ex = await Assert.ThrowsAsync<DataSourceException>(() =>
            session.QueryRowsAsync($"SELECT * FROM {SqlServerFixture.TableName}"));

        Assert.Contains("çevrimdışı", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

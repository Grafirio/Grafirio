using Grafirio.Bridge.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.Bridge.Tests;

/// <summary>
/// Bridge'in buluttan gelen sorguyu REDDETTIĞI durumlar.
///
/// Bu üç kontrol müşteriye verilen sözün kendisi ve hepsi veritabanına
/// dokunmadan, sorgu daha çalıştırılmadan uygulanıyor — dolayısıyla ayakta
/// bir SQL Server olmadan ölçülebiliyorlar.
///
/// Tablo izin listesi özellikle önemli: en muhafazakâr BT ekiplerinin
/// isteyeceği kemer bu ve bulut ne gönderirse göndersin burada duruyor.
/// </summary>
public class QueryExecutorGuardTests : IDisposable
{
    private static readonly Guid ConnectionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly string _statePath =
        Path.Combine(Path.GetTempPath(), $"bridge-state-{Guid.NewGuid():N}.dat");

    private readonly string _auditPath =
        Path.Combine(Path.GetTempPath(), $"bridge-audit-{Guid.NewGuid():N}.tsv");

    private QueryExecutor Executor(params string[] allowedTables)
    {
        var state = new BridgeState(NullLogger<BridgeState>.Instance, _statePath);

        state.UpsertConnection(new BridgeConnection
        {
            ConnectionId = ConnectionId,
            Name = "test",
            Host = "yok",
            Database = "yok",
            Username = "yok",
            Password = "yok",
            AllowedTables = allowedTables.ToList(),
        });

        return new QueryExecutor(
            state,
            new QueryAuditLog(NullLogger<QueryAuditLog>.Instance, _auditPath),
            NullLogger<QueryExecutor>.Instance);
    }

    /// <summary>
    /// Zaman aşımı bilerek 1 saniye: kontrolleri GEÇEN sorgular veritabanına
    /// gidiyor ve buradaki host uydurma. Varsayılan 30 saniyeyle bu testler
    /// dakikalarca sürerdi; ölçülen şey zaten bağlantı değil, sorgunun
    /// reddedilip reddedilmediği.
    /// </summary>
    private static ExecuteQueryRequest Request(string sql, Guid? connectionId = null) =>
        new("istek-1", connectionId ?? ConnectionId, sql, [], 1000, TimeoutSeconds);

    private const int TimeoutSeconds = 1;

    private static async Task<QueryFailure> FailureOf(QueryExecutor executor, ExecuteQueryRequest request)
    {
        await foreach (var message in executor.ExecuteAsync(request, CancellationToken.None))
            return Assert.IsType<QueryFailure>(message);

        throw new InvalidOperationException("Hiç mesaj dönmedi.");
    }

    [Fact]
    public async Task Tanimsiz_baglanti_reddediliyor()
    {
        var failure = await FailureOf(
            Executor(), Request("SELECT 1", connectionId: Guid.NewGuid()));

        Assert.Equal(QueryFailure.UnknownConnection, failure.Code);
    }

    [Fact]
    public async Task Yazma_sorgusu_reddediliyor()
    {
        var failure = await FailureOf(Executor(), Request("DROP TABLE dbo.Shipments"));

        Assert.Equal(QueryFailure.NotReadOnly, failure.Code);
    }

    [Fact]
    public async Task Izin_listesi_bossa_tablo_kisiti_yok()
    {
        // Liste boşken kısıt uygulanmıyor; reddedilseydi varsayılan kurulum
        // hiç çalışmazdı. Burada beklenen şey TableNotAllowed OLMAMASI —
        // sorgu veritabanına gidiyor ve bağlantı hatası veriyor.
        var failure = await FailureOf(
            Executor(), Request("SELECT * FROM dbo.HerhangiBirTablo"));

        Assert.NotEqual(QueryFailure.TableNotAllowed, failure.Code);
    }

    [Fact]
    public async Task Izin_listesindeki_tablo_gecebiliyor()
    {
        var failure = await FailureOf(
            Executor("dbo.Shipments"), Request("SELECT * FROM dbo.Shipments"));

        Assert.NotEqual(QueryFailure.TableNotAllowed, failure.Code);
    }

    [Fact]
    public async Task Izin_listesinde_olmayan_tablo_reddediliyor()
    {
        var failure = await FailureOf(
            Executor("dbo.Shipments"), Request("SELECT * FROM dbo.Gizli"));

        Assert.Equal(QueryFailure.TableNotAllowed, failure.Code);
        Assert.Contains("Gizli", failure.Message);
    }

    [Fact]
    public async Task Alt_sorgudaki_izinsiz_tablo_da_reddediliyor()
    {
        // Asıl risk burada: dış sorgu izinli bir tabloya bakarken alt sorgu
        // başka bir tablodan veri çekebilir.
        var failure = await FailureOf(
            Executor("dbo.Shipments"),
            Request("SELECT * FROM dbo.Shipments WHERE Id IN (SELECT Id FROM dbo.Gizli)"));

        Assert.Equal(QueryFailure.TableNotAllowed, failure.Code);
    }

    [Fact]
    public async Task Semasiz_yazilan_tablo_dbo_sayiliyor()
    {
        // İzin listesinde "dbo.Shipments" varken sorguda "Shipments" yazılması
        // reddedilmemeli; ikisi aynı tablo.
        var failure = await FailureOf(
            Executor("dbo.Shipments"), Request("SELECT * FROM Shipments"));

        Assert.NotEqual(QueryFailure.TableNotAllowed, failure.Code);
    }

    [Fact]
    public async Task Sistem_katalogu_izin_listesine_takilmiyor()
    {
        // Şema okumak için gerekli ve müşteri verisi içermiyor. Reddedilseydi
        // izin listesi tanımlayan her müşteride tablo listesi boş gelirdi.
        var failure = await FailureOf(
            Executor("dbo.Shipments"),
            Request("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES"));

        Assert.NotEqual(QueryFailure.TableNotAllowed, failure.Code);
    }

    /// <summary>
    /// Denetim günlüğü müşterinin kendi diskinde duruyor ve amacı "hangi sorgu
    /// koştu" sorusunu cevaplamak. Parametre DEĞERLERİ oraya yazılmamalı: bir
    /// filtre değeri müşteri adı ya da TC kimlik numarası olabilir.
    /// </summary>
    [Fact]
    public async Task Denetim_gunlugu_parametre_degerlerini_yazmiyor()
    {
        var executor = Executor();
        var request = new ExecuteQueryRequest(
            "istek-2", ConnectionId,
            "DELETE FROM dbo.Musteriler WHERE TcKimlik = @p0",
            [new QueryParameter("p0", SqlValueKind.Text, "12345678901")],
            1000, TimeoutSeconds);

        await FailureOf(executor, request);

        var log = await File.ReadAllTextAsync(_auditPath);

        Assert.Contains("REDDEDİLDİ", log);
        Assert.Contains("istek-2", log);
        Assert.DoesNotContain("12345678901", log);
    }

    public void Dispose()
    {
        foreach (var path in new[] { _statePath, _auditPath })
            if (File.Exists(path)) File.Delete(path);
    }
}

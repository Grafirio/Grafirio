using Grafirio.DataAnalysis.Api.Features.Profile;
using Microsoft.Extensions.Logging.Abstractions;

using static Grafirio.DataAnalysis.Tests.FakeDataSourceSession;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Iliski kesfinin veritabanina dokunan ucu — artik sahte bir oturumla,
/// ayakta SQL Server olmadan kosuyor.
///
/// <see cref="RelationshipDiscoveryTests"/> yalnizca aday uretimini (saf
/// fonksiyon) olcuyordu. Burada olculen sey uc kaynagin birlesmesi: bildirilmis
/// yabanci anahtar, ad kalibindan cikarim ve deger ortusmesiyle eleme.
/// </summary>
public class RelationshipDiscoverySessionTests
{
    private static readonly string UniqueIndexQuery = "sys.indexes";
    private static readonly string ForeignKeyQuery = "sys.foreign_keys";
    private static readonly string OverlapQuery = "EXISTS";

    private static RelationshipDiscovery Discovery() =>
        new(NullLogger<RelationshipDiscovery>.Instance);

    private static List<TableProfile> ShipmentsAndCompanies() =>
    [
        new()
        {
            Schema = "dbo",
            TableName = "Shipments",
            Columns =
            [
                new ColumnProfile { ColumnName = "Id", DataType = "int", IsPrimaryKey = true },
                new ColumnProfile { ColumnName = "CompanyId", DataType = "int" },
            ]
        },
        new()
        {
            Schema = "dbo",
            TableName = "Companies",
            Columns =
            [
                new ColumnProfile { ColumnName = "Id", DataType = "int", IsPrimaryKey = true },
                new ColumnProfile { ColumnName = "Unvan", DataType = "nvarchar", DistinctCount = 120 },
            ]
        },
    ];

    private static FakeDataSourceSession SessionWithKeys() =>
        new FakeDataSourceSession()
            .Respond(UniqueIndexQuery,
                Row(("TableName", "dbo.Shipments"), ("ColumnName", "Id")),
                Row(("TableName", "dbo.Companies"), ("ColumnName", "Id")));

    [Fact]
    public async Task Deger_ortusmesi_yuksekse_cikarsanmis_kenar_kabul_edilir()
    {
        var session = SessionWithKeys()
            .Respond(ForeignKeyQuery)                                  // bildirilmis FK yok
            .Respond(OverlapQuery, Row(("Total", 200), ("Matched", 200)));

        var edges = await Discovery().DiscoverAsync(session, ShipmentsAndCompanies());

        var edge = Assert.Single(edges);
        Assert.Equal("dbo.Shipments", edge.FromTable);
        Assert.Equal("CompanyId", Assert.Single(edge.FromColumns));
        Assert.Equal("dbo.Companies", edge.ToTable);
        Assert.Equal("inferred", edge.Source);
        Assert.Equal("high", edge.Confidence);

        // Kullanicinin grafikte kod yerine gormek istedigi kolon.
        Assert.Equal("Unvan", edge.LabelColumn);
    }

    [Fact]
    public async Task Deger_ortusmesi_dusukse_aday_elenir()
    {
        // Ad kalibi tutuyor ama degerler hedefte yok: ad benzerligi tek basina
        // kanit degil, elenmesi gereken durum tam olarak bu.
        var session = SessionWithKeys()
            .Respond(ForeignKeyQuery)
            .Respond(OverlapQuery, Row(("Total", 200), ("Matched", 4)));

        var edges = await Discovery().DiscoverAsync(session, ShipmentsAndCompanies());

        Assert.Empty(edges);
    }

    [Fact]
    public async Task Bildirilmis_yabanci_anahtar_olcum_yapilmadan_kabul_edilir()
    {
        // FK zaten kanittir; ayrica ortusme olculmemeli. Sahte oturum
        // tanimlanmamis sorguda hata verdigi icin, ortusme sorgusu calisirsa
        // test kirmizi doner — kontrol ettigimiz sey bu.
        var session = SessionWithKeys()
            .Respond(ForeignKeyQuery, Row(
                ("ConstraintId", 1),
                ("FromTable", "dbo.Shipments"), ("FromColumn", "CompanyId"),
                ("ToTable", "dbo.Companies"), ("ToColumn", "Id"),
                ("IsNotTrusted", false), ("IsNullable", false), ("Ordinal", 1)));

        var edges = await Discovery().DiscoverAsync(session, ShipmentsAndCompanies());

        var edge = Assert.Single(edges);
        Assert.Equal("fk", edge.Source);
        Assert.True(edge.IsTrusted);
        Assert.False(edge.IsOptional);
        Assert.DoesNotContain(session.ExecutedQueries, q => q.Contains(OverlapQuery));
    }

    [Fact]
    public async Task Olcum_sorgusu_duserse_aday_sessizce_elenir()
    {
        // Tip donusumu tutmayan adayda SQL patlar. Bu bir hata degil, adayin
        // elenmesidir: kesif akisi devam etmeli.
        var session = new ThrowingOverlapSession(SessionWithKeys()
            .Respond(ForeignKeyQuery));

        var edges = await Discovery().DiscoverAsync(session, ShipmentsAndCompanies());

        Assert.Empty(edges);
    }

    /// <summary>Ortusme sorgusunda veri kaynagi hatasi firlatan oturum.</summary>
    private sealed class ThrowingOverlapSession(FakeDataSourceSession inner)
        : Grafirio.DataAnalysis.Api.Data.Access.IDataSourceSession
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(
            string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
        {
            if (sql.Contains(OverlapQuery, StringComparison.OrdinalIgnoreCase))
                throw new Grafirio.DataAnalysis.Api.Data.Access.DataSourceException(
                    "Operand type clash (SQL hata no: 206)");

            return inner.QueryAsync<T>(sql, parameters, timeoutSeconds, ct);
        }

        public Task<IReadOnlyList<Grafirio.DataAnalysis.Api.Data.Access.QueryRow>> QueryRowsAsync(
            string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default) =>
            inner.QueryRowsAsync(sql, parameters, timeoutSeconds, ct);

        public Task<T?> ScalarAsync<T>(
            string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default) =>
            inner.ScalarAsync<T>(sql, parameters, timeoutSeconds, ct);

        public IAsyncEnumerable<Grafirio.DataAnalysis.Api.Data.Access.QueryRow> StreamAsync(
            string sql, object? parameters = null, int? timeoutSeconds = null, int? maxRows = null,
            CancellationToken ct = default) =>
            inner.StreamAsync(sql, parameters, timeoutSeconds, maxRows, ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

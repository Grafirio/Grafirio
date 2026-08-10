using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// Buluttan musteri veritabanina dogrudan TCP acan uygulama — bugunku davranis.
///
/// Yalnizca veritabani buluttan erisilebilir oldugunda calisir. Firewall
/// arkasindaki musteriler icin ikinci bir uygulama (bridge) ayni arayuze
/// oturacak.
/// </summary>
public sealed class DirectDataSourceSession : IDataSourceSession
{
    private readonly SqlConnection _connection;

    private DirectDataSourceSession(SqlConnection connection) => _connection = connection;

    /// <summary>
    /// Uretimde <see cref="DataSourceFactory"/> cagiriyor. Public olmasinin
    /// sebebi parite testi: dogrudan yol ile bridge yolunun ayni sonucu
    /// verdigini olcmek icin ikisinin de disaridan kurulabilmesi gerekiyor.
    /// </summary>
    public static async Task<DirectDataSourceSession> OpenAsync(
        DataSourceTarget target, int connectTimeoutSeconds, CancellationToken ct)
    {
        var connection = new SqlConnection(target.ToConnectionString(connectTimeoutSeconds));
        try
        {
            await connection.OpenAsync(ct);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return new DirectDataSourceSession(connection);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        try
        {
            var rows = await _connection.QueryAsync<T>(Command(sql, parameters, timeoutSeconds, ct));
            return rows.AsList();
        }
        catch (SqlException ex)
        {
            throw Wrap(ex);
        }
    }

    public async Task<IReadOnlyList<QueryRow>> QueryRowsAsync(
        string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        try
        {
            var rows = await _connection.QueryAsync(Command(sql, parameters, timeoutSeconds, ct));

            // Dapper'in satiri zaten IDictionary; DBNull'lari null'a cevirmis
            // oluyor. Kendi tipimize kopyalamak, bridge yolunun ayni sekli
            // uretebilmesi icin gerekli.
            return rows
                .Cast<IDictionary<string, object?>>()
                .Select(row => new QueryRow(
                    row.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)))
                .ToList();
        }
        catch (SqlException ex)
        {
            throw Wrap(ex);
        }
    }

    public async Task<T?> ScalarAsync<T>(
        string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        try
        {
            return await _connection.ExecuteScalarAsync<T>(Command(sql, parameters, timeoutSeconds, ct));
        }
        catch (SqlException ex)
        {
            throw Wrap(ex);
        }
    }

    public async IAsyncEnumerable<QueryRow> StreamAsync(
        string sql,
        object? parameters = null,
        int? timeoutSeconds = null,
        int? maxRows = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        IEnumerable<dynamic> rows;
        try
        {
            // CommandFlags.None = tamponsuz: Dapper satirlari okuyucudan
            // tembelce cekiyor, hepsini birden listeye almiyor.
            rows = await _connection.QueryAsync(new CommandDefinition(
                sql, parameters, commandTimeout: timeoutSeconds,
                flags: CommandFlags.None, cancellationToken: ct));
        }
        catch (SqlException ex)
        {
            throw Wrap(ex);
        }

        var emitted = 0;
        foreach (var row in rows.Cast<IDictionary<string, object?>>())
        {
            if (maxRows is { } cap && emitted >= cap) yield break;

            emitted++;
            yield return new QueryRow(
                row.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static CommandDefinition Command(
        string sql, object? parameters, int? timeoutSeconds, CancellationToken ct) =>
        new(sql, parameters, commandTimeout: timeoutSeconds, cancellationToken: ct);

    /// <summary>
    /// SQL hatasi tasiyicidan bagimsiz tipe sariliyor. Hata numarasi mesajda
    /// kaliyor: "Login failed (18456)" gibi bir satir, tesnisi yapilabilen tek
    /// seydir ve cagiran taraflar bunu kullaniciya gosteriyor.
    /// </summary>
    private static DataSourceException Wrap(SqlException ex) =>
        new($"{ex.Message} (SQL hata no: {ex.Number})", ex);

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}

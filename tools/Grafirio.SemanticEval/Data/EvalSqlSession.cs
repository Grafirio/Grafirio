using System.Runtime.CompilerServices;
using Dapper;
using Grafirio.DataAnalysis.Api.Data.Access;
using Microsoft.Data.SqlClient;

namespace Grafirio.SemanticEval.Data;

/// <summary>
/// Test veritabanina dogrudan baglanan oturum.
///
/// Uretimdeki <see cref="DirectDataSourceSession"/> kullanilmiyor: o, SQL kimlik
/// dogrulamasi ve okuma-only bir kullanici istiyor (dogru olan bu), yerel
/// LocalDB ise Windows kimligiyle calisiyor. Olculen sey profil ve kesif
/// mantigi; baglantinin guvenlik kapilari ayri testlerde sinaniyor.
///
/// Uretimle AYNI davranmasi gereken iki sey:
///   * Satir bicimi (<see cref="QueryRow"/>, DBNull yerine null).
///   * Hata bicimi: SQL hatasi <see cref="DataSourceException"/>'a sariliyor.
///     Kesif kodu bazi hatalari "aday elendi" diye yutuyor; bu oturum farkli
///     davransaydi olcum uretimde olmayan bir davranisi olcerdi.
/// </summary>
public sealed class EvalSqlSession(SqlConnection connection) : IDataSourceSession
{
    public static async Task<EvalSqlSession> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return new EvalSqlSession(connection);
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, int? timeoutSeconds = null,
        CancellationToken ct = default) =>
        Wrap(async () => (IReadOnlyList<T>)(await connection.QueryAsync<T>(Command(sql, parameters, timeoutSeconds, ct))).AsList());

    public Task<IReadOnlyList<QueryRow>> QueryRowsAsync(string sql, object? parameters = null,
        int? timeoutSeconds = null, CancellationToken ct = default) =>
        Wrap(async () =>
        {
            var rows = await connection.QueryAsync(Command(sql, parameters, timeoutSeconds, ct));
            return (IReadOnlyList<QueryRow>)rows.Cast<IDictionary<string, object?>>()
                .Select(row => new QueryRow(row.ToDictionary(kv => kv.Key, kv => kv.Value is DBNull ? null : kv.Value,
                    StringComparer.OrdinalIgnoreCase)))
                .ToList();
        });

    public Task<T?> ScalarAsync<T>(string sql, object? parameters = null, int? timeoutSeconds = null,
        CancellationToken ct = default) =>
        Wrap(() => connection.ExecuteScalarAsync<T?>(Command(sql, parameters, timeoutSeconds, ct)));

    public async IAsyncEnumerable<QueryRow> StreamAsync(string sql, object? parameters = null, int? timeoutSeconds = null,
        int? maxRows = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var emitted = 0;
        foreach (var row in await QueryRowsAsync(sql, parameters, timeoutSeconds, ct))
        {
            if (maxRows is { } cap && emitted++ >= cap) yield break;
            yield return row;
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    /// <summary>Sorgunun SQL hatasi aldigi her sey; tanilama icin rapora yaziliyor.</summary>
    public List<string> SqlErrors { get; } = [];

    private async Task<T> Wrap<T>(Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (SqlException exception)
        {
            SqlErrors.Add($"SQL {exception.Number}: {exception.Message}");
            // DirectDataSourceSession.Wrap ile ayni bicim.
            throw new DataSourceException($"{exception.Message} (SQL hata no: {exception.Number})", exception);
        }
    }

    private static CommandDefinition Command(string sql, object? parameters, int? timeoutSeconds, CancellationToken ct) =>
        new(sql, parameters, commandTimeout: timeoutSeconds ?? 60, cancellationToken: ct);
}

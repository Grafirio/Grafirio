using System.Runtime.CompilerServices;

namespace Grafirio.DataAnalysis.Api.Data.Access;

internal sealed class ConnectionRouteSession(IDataSourceSession session, Func<CancellationToken, Task> validate)
    : IDataSourceSession
{
    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null,
        int? timeoutSeconds = null, CancellationToken ct = default)
    {
        await validate(ct);
        return await session.QueryAsync<T>(sql, parameters, timeoutSeconds, ct);
    }

    public async Task<IReadOnlyList<QueryRow>> QueryRowsAsync(string sql, object? parameters = null,
        int? timeoutSeconds = null, CancellationToken ct = default)
    {
        await validate(ct);
        return await session.QueryRowsAsync(sql, parameters, timeoutSeconds, ct);
    }

    public async Task<T?> ScalarAsync<T>(string sql, object? parameters = null,
        int? timeoutSeconds = null, CancellationToken ct = default)
    {
        await validate(ct);
        return await session.ScalarAsync<T>(sql, parameters, timeoutSeconds, ct);
    }

    public async IAsyncEnumerable<QueryRow> StreamAsync(string sql, object? parameters = null,
        int? timeoutSeconds = null, int? maxRows = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await validate(ct);
        await foreach (var row in session.StreamAsync(sql, parameters, timeoutSeconds, maxRows, ct))
            yield return row;
    }

    public ValueTask DisposeAsync() => session.DisposeAsync();
}
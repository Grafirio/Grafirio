using Grafirio.DataAnalysis.Api.Data.Access;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Veritabani olmadan <see cref="IDataSourceSession"/> yerine gecen sahte oturum.
///
/// Bunun mumkun olmasi <c>IDataSourceSession</c>'in asil kazancı: onceden veriye
/// dokunan her sey <c>SqlConnection</c> aliyordu ve ayakta bir SQL Server
/// olmadan test edilemiyordu.
///
/// Sorgular icerdikleri metne gore eslesiyor: kayit sirasi verilen anahtar
/// kelimenin sorguda gecip gecmedigine bakilarak taraniyor. Beklenmeyen bir
/// sorgu gelirse sessizce bos donmuyor, hata veriyor — testin neyi
/// calistirdigini gormemek en kotu sonuc.
/// </summary>
public sealed class FakeDataSourceSession : IDataSourceSession
{
    private readonly List<(string Keyword, List<Dictionary<string, object?>> Rows)> _responses = [];

    /// <summary>Calistirilan sorgular, sirasiyla. Denetim icin.</summary>
    public List<string> ExecutedQueries { get; } = [];

    /// <summary>
    /// <paramref name="keyword"/> gecen sorgular icin verilen satirlar donsun.
    /// </summary>
    public FakeDataSourceSession Respond(string keyword, params Dictionary<string, object?>[] rows)
    {
        _responses.Add((keyword, rows.ToList()));
        return this;
    }

    public static Dictionary<string, object?> Row(params (string Column, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Column, c => c.Value, StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var rows = Match(sql);

        // Dapper'in yaptigi isin sadelestirilmis hali: kolon adi -> ozellik adi.
        var mapped = rows.Select(row =>
        {
            if (typeof(T) == typeof(string))
                return (T)(object)(row.Values.First()?.ToString() ?? "");

            var instance = Activator.CreateInstance<T>();
            foreach (var (column, value) in row)
            {
                var property = typeof(T).GetProperty(column);
                if (property is null || value is null) continue;

                property.SetValue(instance, Convert.ChangeType(
                    value, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
            }
            return instance;
        }).ToList();

        return Task.FromResult<IReadOnlyList<T>>(mapped);
    }

    public Task<IReadOnlyList<QueryRow>> QueryRowsAsync(
        string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<QueryRow>>(
            Match(sql).Select(row => new QueryRow(row)).ToList());

    public Task<T?> ScalarAsync<T>(
        string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var rows = Match(sql);
        if (rows.Count == 0 || rows[0].Values.First() is not { } value) return Task.FromResult<T?>(default);

        return Task.FromResult((T?)Convert.ChangeType(
            value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T)));
    }

    public async IAsyncEnumerable<QueryRow> StreamAsync(
        string sql, object? parameters = null, int? timeoutSeconds = null, int? maxRows = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var emitted = 0;
        foreach (var row in Match(sql))
        {
            if (maxRows is { } cap && emitted >= cap) yield break;

            emitted++;
            yield return new QueryRow(row);
        }

        await Task.CompletedTask;
    }

    private List<Dictionary<string, object?>> Match(string sql)
    {
        ExecutedQueries.Add(sql);

        foreach (var (keyword, rows) in _responses)
            if (sql.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return rows;

        throw new InvalidOperationException(
            $"Sahte oturuma tanımlanmamış sorgu geldi:\n{sql}");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

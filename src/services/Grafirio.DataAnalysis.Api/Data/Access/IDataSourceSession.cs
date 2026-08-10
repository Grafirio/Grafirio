namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// Musteri veritabanina giden TEK kapi.
///
/// Neden bir baglanti nesnesi degil de bu: musterilerin cogunda veritabani
/// firewall arkasinda ve buluttan erisilemiyor. Cozum, baglantiyi musterinin
/// aginda calisan bir bridge'in DISARI dogru kurmasi. Boyle bir yolda
/// <c>SqlConnection</c> nesnesi tasinamaz — tasinabilecek sey sorgu metni ve
/// donen satirlardir. Soyutlama bu yuzden "baglanti" degil "sorgu calistir"
/// seviyesinde.
///
/// Bugun tek uygulamasi <see cref="DirectDataSourceSession"/> (buluttan
/// dogrudan TCP). Bridge yolu ikinci bir uygulama olarak ayni arayuze oturacak;
/// cagiran taraflarin hicbiri degismeyecek.
///
/// Zaman asimi saniye cinsinden ve isteğe bagli: sema sorgulari uzun surebilir
/// (60 sn), olcum sorgulari kisa tutulur (30 sn).
/// </summary>
public interface IDataSourceSession : IAsyncDisposable
{
    /// <summary>Sonucu verilen tipe esleyerek okur.</summary>
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        int? timeoutSeconds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Kolon adlari onceden bilinmeyen sorgular icin. Onceden bu durumlarda
    /// Dapper'in <c>dynamic</c>'i kullaniliyordu; <c>dynamic</c> bir tel
    /// uzerinden gecemedigi icin satirlar acik bir sozluk olarak veriliyor.
    /// </summary>
    Task<IReadOnlyList<QueryRow>> QueryRowsAsync(
        string sql,
        object? parameters = null,
        int? timeoutSeconds = null,
        CancellationToken ct = default);

    /// <summary>Tek deger donduren sorgular (COUNT, SUM, MIN...).</summary>
    Task<T?> ScalarAsync<T>(
        string sql,
        object? parameters = null,
        int? timeoutSeconds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Satirlari birikitirmeden akitir.
    ///
    /// Model egiten analizler on binlerce satir okuyor; bunlari once listeye
    /// toplayip sonra JSON'a cevirmek, ayni veriyi bellekte iki kez tutmak
    /// demek. Bridge yolunda da satirlar zaten parca parca gelecek — akis, o
    /// protokolun dogal sekli.
    /// </summary>
    IAsyncEnumerable<QueryRow> StreamAsync(
        string sql,
        object? parameters = null,
        int? timeoutSeconds = null,
        int? maxRows = null,
        CancellationToken ct = default);
}

public static class DataSourceSessionExtensions
{
    /// <summary>
    /// Tek satir bekleyen sorgular. Birden fazla satir donerse ilki alinir —
    /// cagiran taraflarin hepsi zaten tek satirlik toplama sorgulari yaziyor.
    /// </summary>
    public static async Task<T?> QueryFirstOrDefaultAsync<T>(
        this IDataSourceSession session,
        string sql,
        object? parameters = null,
        int? timeoutSeconds = null,
        CancellationToken ct = default)
    {
        var rows = await session.QueryAsync<T>(sql, parameters, timeoutSeconds, ct);
        return rows.Count > 0 ? rows[0] : default;
    }

    public static async Task<QueryRow?> QueryFirstRowOrDefaultAsync(
        this IDataSourceSession session,
        string sql,
        object? parameters = null,
        int? timeoutSeconds = null,
        CancellationToken ct = default)
    {
        var rows = await session.QueryRowsAsync(sql, parameters, timeoutSeconds, ct);
        return rows.Count > 0 ? rows[0] : null;
    }
}

/// <summary>
/// Kolon adi -> deger. <c>DBNull</c> hicbir zaman disari cikmaz; bos hucre
/// <c>null</c>'dir.
/// </summary>
public sealed class QueryRow(IReadOnlyDictionary<string, object?> values)
{
    public IReadOnlyDictionary<string, object?> Values { get; } = values;

    public object? this[string column] =>
        Values.TryGetValue(column, out var value) ? value : null;

    public string? GetString(string column) => this[column]?.ToString();

    /// <summary>
    /// Kolonun bulunmasi ZORUNLU oldugu yerler icin. Eksikse sessizce bos
    /// string donmek yerine hata verir: kolon adini yanlis yazmak sonucu
    /// bozar ama fark edilmez.
    /// </summary>
    public string GetRequiredString(string column) =>
        this[column]?.ToString()
        ?? throw new DataSourceException($"Sorgu sonucunda '{column}' kolonu yok ya da boş.");

    public int GetInt32(string column, int fallback = 0) =>
        this[column] is { } value ? Convert.ToInt32(value) : fallback;

    public long GetInt64(string column, long fallback = 0) =>
        this[column] is { } value ? Convert.ToInt64(value) : fallback;
}

/// <summary>
/// Veri kaynagina ulasirken olusan hata. Turu bilerek tasiyicidan bagimsiz:
/// dogrudan baglantida <c>SqlException</c>, bridge yolunda bir protokol hatasi
/// olacak. Cagiran taraf ikisini de ayni sekilde ele alabilsin diye ikisi de
/// buna sarilir.
/// </summary>
public class DataSourceException(string message, Exception? inner = null)
    : Exception(message, inner);

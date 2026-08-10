using System.Runtime.CompilerServices;
using Grafirio.Bridge.Contracts;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.SignalR;

namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// Sorguyu musteri agindaki bridge'e gonderip satirlari geri alan oturum.
///
/// <see cref="DirectDataSourceSession"/> ile ayni arayuz: Faz 1'de porta
/// tasinan sekiz cagri noktasinin hicbiri bu iki yolu birbirinden ayirt
/// etmiyor. Buradaki tek fark, veritabanina baglanan tarafin bulut degil
/// musterinin kendi sunucusu olmasi.
///
/// Baglanti dizesi bu yolda bulutta hic olusmuyor; kimlik bilgisi musterinin
/// diskinde duruyor.
/// </summary>
public sealed class BridgeDataSourceSession(
    Guid bridgeId,
    Guid connectionId,
    string companyId,
    IHubContext<BridgeHub> hub,
    BridgeRegistry registry,
    ILogger<BridgeDataSourceSession> logger) : IDataSourceSession
{
    /// <summary>Tek sorguda alinabilecek en fazla satir.</summary>
    private const int DefaultMaxRows = 100_000;

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var rows = await QueryRowsAsync(sql, parameters, timeoutSeconds, ct);
        return rows.Select(QueryRowMapper.Map<T>).ToList();
    }

    public async Task<IReadOnlyList<QueryRow>> QueryRowsAsync(
        string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var rows = new List<QueryRow>();
        await foreach (var row in StreamAsync(sql, parameters, timeoutSeconds, null, ct))
            rows.Add(row);

        return rows;
    }

    public async Task<T?> ScalarAsync<T>(
        string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        await foreach (var row in StreamAsync(sql, parameters, timeoutSeconds, maxRows: 1, ct))
        {
            var value = row.Values.Values.FirstOrDefault();
            if (value is null) return default;

            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(value, target);
        }

        return default;
    }

    public async IAsyncEnumerable<QueryRow> StreamAsync(
        string sql,
        object? parameters = null,
        int? timeoutSeconds = null,
        int? maxRows = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Sorgu bridge'e gitmeden once burada da eleniyor. Bridge kendi
        // tarafinda tekrar bakacak — ikisi de gerekli: buradaki hatayi erken
        // gosteriyor, oradaki musteriye verilen sozu tutuyor.
        if (!ReadOnlySqlPolicy.IsReadOnly(sql))
            throw new DataSourceException("Yalnızca okuma sorguları çalıştırılabilir.");

        var requestId = Guid.NewGuid().ToString("N");
        var connectionIdOfBridge = Resolve();
        var pending = registry.Register(requestId, bridgeId);

        try
        {
            await hub.Clients.Client(connectionIdOfBridge).SendAsync(
                BridgeProtocol.ServerToBridge.ExecuteQuery,
                new ExecuteQueryRequest(
                    requestId,
                    connectionId,
                    sql,
                    QueryParameterCodec.Encode(parameters),
                    maxRows ?? DefaultMaxRows,
                    timeoutSeconds ?? DataSourceTarget.DefaultConnectTimeoutSeconds),
                ct);

            var emitted = 0;

            await foreach (var chunk in ReadChunksAsync(pending, requestId, ct))
            {
                foreach (var row in chunk.Rows)
                {
                    if (maxRows is { } cap && emitted >= cap) yield break;

                    emitted++;
                    yield return Materialize(chunk.Columns, row);
                }
            }

            if (pending.Result is { Truncated: true })
                logger.LogWarning(
                    "Bridge sonucu satır tavanına dayandı. Bağlantı: {ConnectionId}", connectionId);
        }
        finally
        {
            registry.Release(requestId);
        }
    }

    /// <summary>
    /// Kanaldan okurken cikan hatalar port'un kendi hata tipine sariliyor;
    /// cagiran taraflar dogrudan baglantiyla ayni sekilde ele alabilsin diye.
    /// </summary>
    private static async IAsyncEnumerable<QueryChunk> ReadChunksAsync(
        PendingQuery pending, string requestId, [EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            QueryChunk chunk;
            try
            {
                if (!await pending.Reader.WaitToReadAsync(ct)) yield break;
                if (!pending.Reader.TryRead(out chunk!)) continue;
            }
            catch (BridgeQueryException ex)
            {
                throw new DataSourceException($"{ex.Message} ({ex.Code})", ex);
            }
            catch (BridgeUnavailableException ex)
            {
                throw new DataSourceException(ex.Message, ex);
            }

            yield return chunk;
        }
    }

    private string Resolve()
    {
        try
        {
            return registry.ConnectionIdOf(bridgeId, companyId);
        }
        catch (BridgeUnavailableException ex)
        {
            throw new DataSourceException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Tel uzerindeki satiri (kolon basina tur + metin deger) CLR degerlerine
    /// cevirir. Turun kolon basina tasinmasinin sebebi burasi: her sey metne
    /// dususe sema profilindeki min/max karsilastirmasi metin sirasina duser.
    /// </summary>
    private static QueryRow Materialize(IReadOnlyList<QueryColumn> columns, string?[] cells)
    {
        var values = new Dictionary<string, object?>(columns.Count, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < columns.Count; i++)
            values[columns[i].Name] = i < cells.Length
                ? SqlValueCodec.Decode(columns[i].Kind, cells[i])
                : null;

        return new QueryRow(values);
    }

    // Oturum durum tutmuyor: her sorgu kendi istek kimligiyle gidiyor.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

using System.Data;
using Dapper;
using Grafirio.Bridge.Contracts;
using Microsoft.Data.SqlClient;

namespace Grafirio.Bridge;

/// <summary>
/// Buluttan gelen sorguyu musteri veritabaninda calistirir.
///
/// <b>Bulut burada guvenilir taraf degildir.</b> Musteriye "buluttan gelen
/// sorgu veritabaninizi degistiremez" deniyorsa, o kontrolun musterinin kendi
/// makinesinde olmasi gerekir — sunucu tarafindaki ayni kontrol yalnizca
/// hatayi erken gostermek icin var.
///
/// Uygulanan kemerler:
///   * Yalnizca SELECT/WITH.
///   * Tablo izin listesi (yapilandirilmissa).
///   * Satir tavani ve sorgu zaman asimi.
///   * Her sorgu yerel denetim gunlugune yaziliyor — musterinin kendi diskinde,
///     bizim erisimimiz olmadan.
/// </summary>
public class QueryExecutor(
    BridgeState state,
    QueryAuditLog audit,
    ILogger<QueryExecutor> logger)
{
    /// <summary>Tek parcada gonderilen satir sayisi.</summary>
    public const int ChunkSize = 500;

    /// <summary>Yerelde uygulanan mutlak satir tavani.</summary>
    private const int HardRowLimit = 200_000;

    /// <summary>Sorgu zaman asimi tavani (saniye).</summary>
    private const int MaxTimeoutSeconds = 600;

    /// <summary>
    /// Sorguyu calistirir ve satirlari parca parca verir.
    ///
    /// Reddedilen sorgu istisna firlatmiyor; <see cref="QueryFailure"/>
    /// donuyor — cagiran taraf bunu oldugu gibi sunucuya iletiyor ve
    /// kullanici sebebi goruyor.
    /// </summary>
    public async IAsyncEnumerable<object> ExecuteAsync(
        ExecuteQueryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var connection = state.FindConnection(request.ConnectionId);

        if (connection is null)
        {
            audit.Rejected(request, "bağlantı tanımlı değil");
            yield return new QueryFailure(request.RequestId, QueryFailure.UnknownConnection,
                "Bu bağlantı bridge üzerinde tanımlı değil. Bridge yapılandırmasını kontrol edin.");
            yield break;
        }

        if (!ReadOnlySql.IsReadOnly(request.Sql))
        {
            // Bu satir loglaniyor: buluttan okuma disi bir sorgu gelmesi
            // normal bir durum degil ve musterinin gormesi gereken bir sey.
            logger.LogWarning("Okuma dışı sorgu reddedildi. İstek: {RequestId}", request.RequestId);
            audit.Rejected(request, "okuma dışı sorgu");

            yield return new QueryFailure(request.RequestId, QueryFailure.NotReadOnly,
                "Yalnızca okuma sorguları çalıştırılabilir.");
            yield break;
        }

        if (DisallowedTable(connection, request.Sql) is { } blocked)
        {
            logger.LogWarning(
                "İzin listesinde olmayan tabloya sorgu reddedildi: {Table}", blocked);
            audit.Rejected(request, $"izin listesinde olmayan tablo: {blocked}");

            yield return new QueryFailure(request.RequestId, QueryFailure.TableNotAllowed,
                $"'{blocked}' tablosu bu bağlantı için izin listesinde değil.");
            yield break;
        }

        await foreach (var message in RunAsync(request, connection, ct))
            yield return message;
    }

    private async IAsyncEnumerable<object> RunAsync(
        ExecuteQueryRequest request,
        BridgeConnection connection,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var rowLimit = Math.Clamp(request.MaxRows, 1, HardRowLimit);
        var timeout = Math.Clamp(request.TimeoutSeconds, 1, MaxTimeoutSeconds);

        await using var sql = new SqlConnection(BuildConnectionString(connection, timeout));

        // Okuyucuyu acmak ile satirlari okumak ayri denemeler: `catch` icinde
        // `yield return` yazilamadigi icin hata once degiskene aliniyor.
        SqlDataReader? reader = null;
        QueryFailure? openFailure = null;

        try
        {
            await sql.OpenAsync(ct);

            var command = new CommandDefinition(
                request.Sql, ToDapperParameters(request.Parameters),
                commandTimeout: timeout, flags: CommandFlags.None, cancellationToken: ct);

            reader = (SqlDataReader)await sql.ExecuteReaderAsync(command);
        }
        catch (Exception ex)
        {
            var (code, message) = Classify(ex);
            logger.LogWarning(ex, "Sorgu çalıştırılamadı. İstek: {RequestId}", request.RequestId);
            audit.Failed(request, message);

            openFailure = new QueryFailure(request.RequestId, code, message);
        }

        if (openFailure is not null)
        {
            yield return openFailure;
            yield break;
        }

        var columns = ReadColumns(reader!);
        var sequence = 0;
        var total = 0;
        var truncated = false;
        var buffer = new List<string?[]>(ChunkSize);

        await using (reader!)
        {
            while (await reader.ReadAsync(ct))
            {
                if (total >= rowLimit) { truncated = true; break; }

                var cells = new string?[columns.Count];
                for (var i = 0; i < columns.Count; i++)
                    cells[i] = SqlValueCodec.Encode(reader.IsDBNull(i) ? null : reader.GetValue(i));

                buffer.Add(cells);
                total++;

                if (buffer.Count < ChunkSize) continue;

                yield return new QueryChunk(request.RequestId, sequence++, columns, buffer);
                buffer = new List<string?[]>(ChunkSize);
            }
        }

        if (buffer.Count > 0)
            yield return new QueryChunk(request.RequestId, sequence, columns, buffer);

        audit.Completed(request, total, truncated);
        yield return new QueryCompleted(request.RequestId, total, truncated, columns);
    }

    /// <summary>
    /// Kolonlarin adi ve turu. Tur burada okunuyor cunku tel uzerinde her sey
    /// metne dususe bulut tarafinda sayilar metin gibi siralanir.
    /// </summary>
    private static List<QueryColumn> ReadColumns(IDataRecord reader)
    {
        var columns = new List<QueryColumn>(reader.FieldCount);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);

            // Adsiz kolon (`SELECT COUNT(*)`) sozlukte kaybolur; sirasiyla
            // adlandiriliyor.
            if (string.IsNullOrEmpty(name)) name = $"Column{i}";

            columns.Add(new QueryColumn(name, SqlValueCodec.KindOf(reader.GetFieldType(i))));
        }

        return columns;
    }

    /// <summary>
    /// Izin listesindeki tablolardan biri olmayan bir tabloya dokunuluyor mu.
    ///
    /// Kontrol kaba: sorgu metninde gecen tablo benzeri adlar taraniyor. Amac
    /// bir SQL ayristiricisi yazmak degil, yanlislikla ya da kotu niyetle
    /// baska bir tabloya gidilmesini engellemek. Suphede kalirsa REDDEDIYOR.
    /// </summary>
    private static string? DisallowedTable(BridgeConnection connection, string sql)
    {
        if (connection.AllowedTables.Count == 0) return null;

        var allowed = connection.AllowedTables
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var referenced in SqlTableScanner.ReferencedTables(sql))
        {
            var normalized = Normalize(referenced);

            // Sistem katalogu sorgulari (sys.*, INFORMATION_SCHEMA.*) sema
            // okumak icin gerekli ve musteri verisi icermiyor.
            if (normalized.StartsWith("sys.", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("information_schema.", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!allowed.Contains(normalized)) return referenced;
        }

        return null;
    }

    private static string Normalize(string table)
    {
        var cleaned = table.Replace("[", "").Replace("]", "").Trim();
        return cleaned.Contains('.') ? cleaned : $"dbo.{cleaned}";
    }

    private static (string Code, string Message) Classify(Exception ex) => ex switch
    {
        SqlException { Number: -2 } => (QueryFailure.Timeout, "Sorgu zaman aşımına uğradı."),
        SqlException sql => (QueryFailure.DatabaseError,
            $"{sql.Message} (SQL hata no: {sql.Number})"),
        OperationCanceledException => (QueryFailure.Timeout, "Sorgu iptal edildi."),
        _ => (QueryFailure.DatabaseError, ex.Message),
    };

    /// <summary>
    /// Tel uzerindeki parametreleri Dapper'in anlayacagi bicime cevirir.
    /// Liste parametreleri korunuyor: iliski kesfi <c>IN @Names</c> yaziyor ve
    /// Dapper bunu tek tek parametrelere aciyor.
    /// </summary>
    private static DynamicParameters ToDapperParameters(IReadOnlyList<QueryParameter> parameters)
    {
        var result = new DynamicParameters();

        foreach (var parameter in parameters)
        {
            if (parameter.Values is { } values)
            {
                result.Add(parameter.Name,
                    values.Select(v => SqlValueCodec.Decode(parameter.Kind, v)).ToList());
                continue;
            }

            result.Add(parameter.Name, SqlValueCodec.Decode(parameter.Kind, parameter.Value));
        }

        return result;
    }

    private static string BuildConnectionString(BridgeConnection connection, int timeoutSeconds) =>
        new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{connection.Host},{connection.Port}",
            InitialCatalog = connection.Database,
            UserID = connection.Username,
            Password = connection.Password,
            IntegratedSecurity = false,
            TrustServerCertificate = connection.TrustServerCertificate,
            ConnectTimeout = Math.Min(timeoutSeconds, 30),
            Encrypt = true,
            MultipleActiveResultSets = true,
            Pooling = true,
            // Musteri veritabaninda kim oldugumuz gorunsun: DBA'in
            // "bu sorgular nereden geliyor" sorusunun cevabi.
            ApplicationName = "Grafirio Bridge",
        }.ConnectionString;
}

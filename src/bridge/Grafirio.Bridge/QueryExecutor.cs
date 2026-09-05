using System.Data;
using System.Data.Common;
using Dapper;
using Grafirio.Bridge.Contracts;
using Grafirio.QueryPolicy;
using Microsoft.Data.SqlClient;
using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

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
            logger.LogWarning("Non-read-only SQL rejected for request {RequestId}", request.RequestId);
            audit.Rejected(request, "okuma dışı sorgu");

            yield return new QueryFailure(request.RequestId, QueryFailure.NotReadOnly,
                "Yalnızca okuma sorguları çalıştırılabilir.");
            yield break;
        }

        QueryValidationResult? validation = null;
        QueryFailure? policyFailure = null;
        try
        {
            validation = SqlPolicy.Validate(request.Sql, connection.AllowedTables, allowMetadata: true);
        }
        catch (QueryPolicyException exception)
        {
            logger.LogWarning("SQL policy rejected request {RequestId}: {Reason}",
                request.RequestId, exception.Message);
            audit.Rejected(request, exception.Message);
            policyFailure = new QueryFailure(request.RequestId,
                exception.TableNotAllowed ? QueryFailure.TableNotAllowed : QueryFailure.NotReadOnly,
                exception.Message);
        }

        if (policyFailure is not null)
        {
            yield return policyFailure;
            yield break;
        }

        await foreach (var message in RunAsync(request, connection, validation!, ct))
            yield return message;
    }

    private async IAsyncEnumerable<object> RunAsync(
        ExecuteQueryRequest request,
        BridgeConnection connection,
        QueryValidationResult validation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var rowLimit = Math.Clamp(request.MaxRows, 1, HardRowLimit);
        var timeout = Math.Clamp(request.TimeoutSeconds, 1, MaxTimeoutSeconds);

        await using var sql = new SqlConnection(BuildConnectionString(connection, timeout));

        // Okuyucuyu acmak ile satirlari okumak ayri denemeler: `catch` icinde
        // `yield return` yazilamadigi icin hata once degiskene aliniyor.
        //
        // Tip `DbDataReader`, `SqlDataReader` DEGIL: Dapper okuyucuyu kendi
        // `DbWrappedReader` sinifiyla sariyor ve somut tipe cevirmek her
        // sorguda InvalidCastException veriyordu.
        DbDataReader? reader = null;
        QueryFailure? openFailure = null;

        try
        {
            await sql.OpenAsync(ct);
            await ReadOnlyPrincipalGuard.VerifyAsync(sql, ct);
            await ReadOnlyPrincipalGuard.VerifyTablesAsync(sql, validation, ct);

            var command = new CommandDefinition(
                request.Sql, ToDapperParameters(request.Parameters),
                commandTimeout: timeout, flags: CommandFlags.None, cancellationToken: ct);

            reader = await sql.ExecuteReaderAsync(command);
        }
        catch (Exception ex)
        {
            var (code, message) = Classify(ex);
            logger.LogWarning(ex, "SQL execution failed for request {RequestId}", request.RequestId);
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

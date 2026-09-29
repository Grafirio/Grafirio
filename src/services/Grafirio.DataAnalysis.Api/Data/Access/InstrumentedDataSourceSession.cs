using System.Diagnostics;
using System.Runtime.CompilerServices;
using Grafirio.DataAnalysis.Api.Infrastructure.Telemetry;
using Grafirio.Telemetry;

namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// Musteri veritabanina giden her sorgunun suresini ve satir sayisini olcer.
///
/// Iki yol (dogrudan / bridge) ayni arayuzu uyguladigi icin olcum tek yerde,
/// fabrikanin dondurdugu oturumun etrafinda. Rota etiketi sayesinde "bridge
/// yolu ne kadar pahali" sorusu ayni olcumden cevaplaniyor.
///
/// SQL metni etikete yazilmiyor: musteri semasini tasir ve her sorgu ayri bir
/// seri acar. Izde yalnizca uzunlugu var.
/// </summary>
public sealed class InstrumentedDataSourceSession(IDataSourceSession inner, string route) : IDataSourceSession
{
    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null,
        int? timeoutSeconds = null, CancellationToken ct = default) =>
        MeasureAsync("query", sql, () => inner.QueryAsync<T>(sql, parameters, timeoutSeconds, ct), r => r.Count);

    public Task<IReadOnlyList<QueryRow>> QueryRowsAsync(string sql, object? parameters = null,
        int? timeoutSeconds = null, CancellationToken ct = default) =>
        MeasureAsync("rows", sql, () => inner.QueryRowsAsync(sql, parameters, timeoutSeconds, ct), r => r.Count);

    public Task<T?> ScalarAsync<T>(string sql, object? parameters = null,
        int? timeoutSeconds = null, CancellationToken ct = default) =>
        MeasureAsync("scalar", sql, () => inner.ScalarAsync<T>(sql, parameters, timeoutSeconds, ct), _ => 1);

    public async IAsyncEnumerable<QueryRow> StreamAsync(string sql, object? parameters = null,
        int? timeoutSeconds = null, int? maxRows = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = Start("stream", sql);
        var started = Stopwatch.GetTimestamp();
        var rows = 0L;

        // Cagiran akisi yarida birakirsa (ilk N satiri alip cikarsa) bu "ok"
        // sayiliyor — hata degil, tuketicinin tercihi. Bu yuzden varsayilan
        // "ok"; yalnizca MoveNext'in kendisi patlarsa degisiyor. yield ile
        // catch ayni blokta yazilamadigi icin numaralandirici elle yuruyor.
        var outcome = "ok";
        var enumerator = inner.StreamAsync(sql, parameters, timeoutSeconds, maxRows, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                }
                catch (OperationCanceledException)
                {
                    outcome = "cancelled";
                    throw;
                }
                catch (Exception exception)
                {
                    outcome = "error";
                    activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                    throw;
                }

                rows++;
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
            Record("stream", started, rows, outcome, activity);
        }
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    private async Task<TResult> MeasureAsync<TResult>(string method, string sql, Func<Task<TResult>> run,
        Func<TResult, long> countRows)
    {
        using var activity = Start(method, sql);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await run();
            Record(method, started, countRows(result), "ok", activity);
            return result;
        }
        catch (OperationCanceledException)
        {
            Record(method, started, 0, "cancelled", activity);
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            Record(method, started, 0, "error", activity);
            throw;
        }
    }

    private Activity? Start(string method, string sql)
    {
        var activity = AnalysisTelemetry.Source.StartActivity("datasource." + method, ActivityKind.Client);
        activity?.SetTag("db.system.name", "microsoft.sql_server");
        activity?.SetTag("grafirio.datasource.route", route);
        activity?.SetTag("grafirio.datasource.sql_length", sql.Length);
        return activity;
    }

    private void Record(string method, long started, long rows, string outcome, Activity? activity)
    {
        var tags = new TagList
        {
            { "datasource.route", route },
            { "datasource.method", method },
            { "datasource.outcome", outcome }
        };

        AnalysisTelemetry.DataSourceDuration.Record(GrafirioTelemetry.Seconds(started), tags);
        if (outcome == "ok") AnalysisTelemetry.DataSourceRows.Record(rows, tags);
        activity?.SetTag("grafirio.datasource.rows", rows);
    }
}

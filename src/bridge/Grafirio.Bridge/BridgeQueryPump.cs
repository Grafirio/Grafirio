using Grafirio.Bridge.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace Grafirio.Bridge;

/// <summary>
/// Buluttan gelen sorgu isteklerini karsilar ve cevabi geri akitir.
///
/// <see cref="BridgeWorker"/>'dan ayri duruyor cunku worker'in isi baglantiyi
/// kurup ayakta tutmak; buradaki is ise protokolun kendisi. Ayrilmasinin somut
/// sebebi test: parite testi bu sinifi oldugu gibi kullaniyor, yani olculen sey
/// uretimde kosan kodun kendisi oluyor — testin icin yeniden yazilmis bir
/// benzeri degil.
/// </summary>
public class BridgeQueryPump(QueryExecutor executor, ILogger<BridgeQueryPump> logger)
{
    private int _activeQueries;

    public int ActiveQueryCount => _activeQueries;

    /// <summary>Kanali dinlemeye baslar.</summary>
    public IDisposable Attach(HubConnection connection, CancellationToken ct) =>
        connection.On<ExecuteQueryRequest>(
            BridgeProtocol.ServerToBridge.ExecuteQuery,
            async request => await HandleAsync(connection, request, ct));

    public async Task HandleAsync(
        HubConnection connection, ExecuteQueryRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _activeQueries);

        try
        {
            await foreach (var message in executor.ExecuteAsync(request, ct))
            {
                switch (message)
                {
                    case QueryChunk chunk:
                        await connection.InvokeAsync(
                            BridgeProtocol.BridgeToServer.PushChunk, chunk, ct);
                        break;

                    case QueryCompleted completed:
                        await connection.InvokeAsync(
                            BridgeProtocol.BridgeToServer.CompleteQuery, completed, ct);
                        break;

                    case QueryFailure failure:
                        await connection.InvokeAsync(
                            BridgeProtocol.BridgeToServer.FailQuery, failure, ct);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Buraya dusen sey beklenmeyen bir hata. Sunucu tarafi sonsuza
            // kadar beklemesin diye yine de bildiriliyor: teshis edilemeyen bir
            // askida kalma, gorulebilen bir hatadan cok daha kotudur.
            logger.LogError(ex, "Sorgu işlenirken beklenmeyen hata. İstek: {RequestId}",
                request.RequestId);

            try
            {
                await connection.InvokeAsync(
                    BridgeProtocol.BridgeToServer.FailQuery,
                    new QueryFailure(request.RequestId, QueryFailure.DatabaseError, ex.Message),
                    ct);
            }
            catch (Exception reportFailure)
            {
                logger.LogError(reportFailure, "Hata sunucuya bildirilemedi.");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeQueries);
        }
    }
}

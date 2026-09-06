using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace Grafirio.Bridge.Desktop;

public sealed class LoopbackListener : IDisposable
{
    private const string CallbackPath = "/callback";
    private readonly HttpListener _listener = new();
    public string RedirectUri { get; }

    public LoopbackListener()
    {
        // HttpListener cannot report an automatically assigned port.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        RedirectUri = $"http://127.0.0.1:{port}{CallbackPath}";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
    }

    public async Task<Callback> WaitForCallbackAsync(
        string expectedState, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var context = await _listener.GetContextAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
                var request = context.Request;
                if (request.HttpMethod != "GET" || request.Url?.AbsolutePath != CallbackPath)
                {
                    await RespondAsync(context.Response, HttpStatusCode.NotFound,
                        "Geçersiz giriş adresi", deadline.Token).ConfigureAwait(false);
                    continue;
                }

                var query = HttpUtility.ParseQueryString(request.Url.Query);
                var state = query.GetValues("state");
                var codes = query.GetValues("code");
                var errors = query.GetValues("error");
                var validState = state is { Length: 1 } && state[0] == expectedState;
                var hasCode = codes is { Length: 1 } && !string.IsNullOrWhiteSpace(codes[0]) && errors is null;
                var hasError = errors is { Length: 1 } && !string.IsNullOrWhiteSpace(errors[0]) && codes is null;
                if (!validState || (!hasCode && !hasError))
                {
                    await RespondAsync(context.Response, HttpStatusCode.BadRequest,
                        "Geçersiz giriş yanıtı", deadline.Token).ConfigureAwait(false);
                    continue;
                }

                await RespondAsync(context.Response, HttpStatusCode.OK,
                    hasError ? "Giriş tamamlanmadı" : "Giriş yanıtı alındı", deadline.Token).ConfigureAwait(false);
                return new Callback(hasCode ? codes![0] : null, state![0], hasError ? errors![0] : null);
            }
        }
        finally
        {
            // Cancellation must also release the pending native listener operation.
            _listener.Stop();
        }
    }

    private static async Task RespondAsync(
        HttpListenerResponse response, HttpStatusCode status, string title, CancellationToken ct)
    {
        var page = $$"""
            <!doctype html><html lang="tr"><head><meta charset="utf-8"><title>Grafirio</title>
            <style>body{font-family:system-ui;background:#f5f6f8;color:#1b2430;text-align:center;padding:80px}
            main{background:white;padding:40px;border-radius:12px;max-width:480px;margin:auto}</style></head>
            <body><main><h1>{{title}}</h1><p>Grafirio uygulamasına dönebilirsiniz. Bu sekmeyi kapatabilirsiniz.</p></main></body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(page);
        response.StatusCode = (int)status;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.ContentLength64 = bytes.Length;
        try
        {
            await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }
        finally
        {
            response.Close();
        }
    }

    public void Dispose() => _listener.Close();

    public sealed record Callback(string? Code, string? State, string? Error)
    {
        public override string ToString() => "Callback { Values = [REDACTED] }";
    }
}
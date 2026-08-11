using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Web;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Tarayicida giris — OAuth 2.0 Authorization Code + PKCE, loopback donusuyle
/// (RFC 8252, "OAuth 2.0 for Native Apps").
///
/// Servis surumu device flow kullaniyor cunku orada ekran olmayabiliyor.
/// Masaustunde kullanici zaten ekranin basinda ve device flow'un bedeli
/// gorunuyor: ekranda bir kod, tarayicida bir kod girme adimi ve sonunda
/// uygulamaya DONMEYEN bir sayfa — cunku o akis, tarayiciyi cihaza geri
/// dondurmez.
///
/// Burada donus var: Keycloak tarayiciyi bu uygulamanin actigi yerel adrese
/// yonlendiriyor, uygulama yonlendirmeyi yakalayip kendini one aliyor.
/// Kullanicinin gordugu tek Keycloak ekrani giris formu, o da zaten Grafirio
/// temasinda.
///
/// Dinleyici yalnizca 127.0.0.1'e acik ve tek bir istek karsilayip kapaniyor.
/// </summary>
public class BrowserLogin(IOptions<BridgeOptions> options, ILogger<BrowserLogin> logger)
{
    private readonly BridgeOptions _options = options.Value;

    /// <summary>Kullanicinin girisi tamamlamasi icin beklenen sure.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public async Task<UserSession?> TryLoginAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.IdentityUrl))
        {
            logger.LogError(
                "IdentityUrl tanımlı değil; giriş yapılacak adres bilinmiyor. " +
                "{Path} dosyasına kimlik sunucusunun adresini yazın.",
                _options.ConfigurationPath);
            return null;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var endpoints = await DiscoverAsync(client, ct);
        if (endpoints is null) return null;

        // Port isletim sistemine sectiriliyor: sabit bir port, o portu baska
        // bir program tutuyorsa girisi tamamen engellerdi.
        using var listener = new LoopbackListener();

        var verifier = RandomUrlSafe(64);
        var state = RandomUrlSafe(32);

        var authorizeUrl = BuildAuthorizeUrl(
            endpoints.Value.Authorization, listener.RedirectUri, verifier, state);

        logger.LogInformation("Tarayıcıda giriş bekleniyor.");
        OpenBrowser(authorizeUrl);

        var callback = await listener.WaitForCallbackAsync(Timeout, ct);
        if (callback is null) return null;

        // state karsilastirmasi: yonlendirmeyi baskasinin baslatmadigindan
        // emin olmanin yolu. Sabit sureli karsilastirma gerekmiyor, deger
        // gizli degil — tek isi bu turu bizim baslattigimizi dogrulamak.
        if (callback.State != state)
        {
            logger.LogError("Giriş yanıtı bu oturuma ait değil; yok sayıldı.");
            return null;
        }

        if (callback.Error is { } error)
        {
            logger.LogError("Giriş tamamlanmadı: {Error} {Description}",
                error, callback.ErrorDescription);
            return null;
        }

        if (callback.Code is not { } code)
        {
            logger.LogError("Giriş yanıtında kod yok.");
            return null;
        }

        return await ExchangeAsync(
            client, endpoints.Value.Token, code, verifier, listener.RedirectUri, ct);
    }

    /// <summary>
    /// Uc adresleri kesif belgesinden okunuyor, elle kurulmuyor: Keycloak'in
    /// yol duzeni bize ait degil ve sabit yazilirsa surum degisiminde sessizce
    /// 404 alinir.
    /// </summary>
    private async Task<(string Authorization, string Token)?> DiscoverAsync(
        HttpClient client, CancellationToken ct)
    {
        var url = _options.IdentityUrl.TrimEnd('/') + "/.well-known/openid-configuration";

        try
        {
            var document = await client.GetFromJsonAsync<DiscoveryDocument>(url, ct);

            if (document?.AuthorizationEndpoint is not { } authorization
                || document.TokenEndpoint is not { } token)
            {
                logger.LogError("Kimlik sunucusu beklenen uçları bildirmiyor ({Url}).", url);
                return null;
            }

            return (authorization, token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kimlik sunucusuna ulaşılamadı: {Url}", url);
            return null;
        }
    }

    private string BuildAuthorizeUrl(
        string endpoint, string redirectUri, string verifier, string state)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        query["client_id"] = _options.InstallerClientId;
        query["response_type"] = "code";
        query["redirect_uri"] = redirectUri;
        query["scope"] = "openid profile email";
        query["state"] = state;
        // PKCE: istemci sirri olmayan bir uygulamada, yakalanan bir kodun
        // baskasi tarafindan token'a cevrilmesini engelleyen sey bu.
        query["code_challenge"] = Challenge(verifier);
        query["code_challenge_method"] = "S256";

        return $"{endpoint}?{query}";
    }

    private async Task<UserSession?> ExchangeAsync(
        HttpClient client,
        string tokenEndpoint,
        string code,
        string verifier,
        string redirectUri,
        CancellationToken ct)
    {
        try
        {
            var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = _options.InstallerClientId,
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["code_verifier"] = verifier,
                }), ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Token alınamadı ({Status}): {Body}", response.StatusCode, body);
                return null;
            }

            var token = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(body);

            if (token?.AccessToken is not { } accessToken)
            {
                logger.LogError("Token yanıtı okunamadı.");
                return null;
            }

            logger.LogInformation("Giriş tamamlandı.");

            return new UserSession(accessToken, token.RefreshToken, token.IdToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Token alınırken kimlik sunucusuna ulaşılamadı.");
            return null;
        }
    }

    private static void OpenBrowser(string url) => Browser.Open(url);

    /// <summary>PKCE dogrulayicisinin S256 ozeti.</summary>
    private static string Challenge(string verifier) =>
        UrlSafe(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string RandomUrlSafe(int bytes) =>
        UrlSafe(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>base64url — RFC 7636'nin istedigi bicim.</summary>
    private static string UrlSafe(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class DiscoveryDocument
    {
        [JsonPropertyName("authorization_endpoint")]
        public string? AuthorizationEndpoint { get; set; }

        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
    }
}

/// <summary>
/// Giris yapan kisinin oturumu.
///
/// Bu, bridge'in kimligi DEGIL: yalnizca kaydi yetkilendiriyor ve panelin
/// uygulama icinde acilmasini sagliyor. Bridge kendi kimligini kayit
/// cevabinda aliyor ve bundan sonrasinda onu kullaniyor — kuran kisi
/// sirketten ayrildiginda bridge calismaya devam ediyor.
/// </summary>
public record UserSession(string AccessToken, string? RefreshToken, string? IdToken);

/// <summary>
/// Yonlendirmeyi karsilayan yerel dinleyici.
///
/// Yalnizca 127.0.0.1'e bagli: makinenin disina acik bir port acmak, kurulum
/// icin gelen birinin firewall'da hicbir sey acmayacagi sozuyle celisirdi.
/// </summary>
public sealed class LoopbackListener : IDisposable
{
    private readonly HttpListener _listener = new();

    public string RedirectUri { get; }

    public LoopbackListener()
    {
        // Port'u once bos bir TcpListener'a sectiriyoruz: HttpListener kendi
        // sectigi portu soylemiyor ve sabit port secmek, o portu baska bir
        // program tuttugunda girisi tamamen engellerdi.
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        RedirectUri = $"http://127.0.0.1:{port}/callback";

        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
    }

    public async Task<Callback?> WaitForCallbackAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            var context = await _listener.GetContextAsync().WaitAsync(deadline.Token);
            var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");

            await RespondAsync(context.Response);

            return new Callback(
                query["code"], query["state"], query["error"], query["error_description"]);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tarayicida kalan sayfa. Uygulama kendini one aliyor ama tarayici
    /// sekmesi de bir sey soylemeli; bos bir sayfa "giris basarisiz mi"
    /// sorusunu doguruyor.
    /// </summary>
    private static async Task RespondAsync(HttpListenerResponse response)
    {
        const string page = """
            <!doctype html>
            <html lang="tr">
            <head>
              <meta charset="utf-8">
              <title>Grafirio</title>
              <style>
                body { margin:0; min-height:100vh; display:flex; align-items:center;
                       justify-content:center; background:#f5f6f8;
                       font-family:'Segoe UI',system-ui,sans-serif; color:#1b2430; }
                .card { text-align:center; padding:40px 48px; background:#fff;
                        border:1px solid #e3e6ea; border-radius:12px; }
                h1 { margin:0 0 8px; font-size:20px; }
                p { margin:0; color:#6b7684; font-size:14px; }
              </style>
            </head>
            <body>
              <div class="card">
                <h1>Giriş tamamlandı</h1>
                <p>Grafirio uygulamasına dönebilirsiniz. Bu sekmeyi kapatabilirsiniz.</p>
              </div>
            </body>
            </html>
            """;

        var bytes = Encoding.UTF8.GetBytes(page);

        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;

        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public void Dispose()
    {
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
    }

    public record Callback(string? Code, string? State, string? Error, string? ErrorDescription);
}

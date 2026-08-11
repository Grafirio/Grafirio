using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge;

/// <summary>
/// Kurulumu yapan kisinin kimligini alir — OAuth 2.0 Device Authorization
/// Grant (RFC 8628) ile.
///
/// Neden: bridge bir sunucu odasinda, cogu zaman tarayicisi olmayan bir
/// makinede kuruluyor. Device flow tam bu durum icin var — ekranda kisa bir
/// kod gosterilir, kuran kisi kendi telefonundan ya da dizustunden onaylar.
///
/// Onceki surumde bunun yerine panelden alinan tek kullanimlik bir token
/// vardi ve <c>appsettings.json</c>'a elle yapistiriliyordu. O token bizim
/// yazdigimiz bir seydi: kendi uretimimiz, kendi ozetimiz, kendi son kullanma
/// mantigimiz. Yaptigi is device flow'un elle yapilmis haliydi ve dosyaya
/// yazilan sir, kurulum bittikten sonra da orada duruyordu.
///
/// <b>Burada alinan token bridge'in kimligi DEGIL.</b> Kuran kisinin kimligi;
/// yalnizca bir kez, <c>/enroll</c> cagrisinda kullaniliyor. Bridge kendi
/// kimligini o cagrinin cevabinda aliyor ve bundan sonrasinda onu kullaniyor.
/// Bu ayrim onemli: kuran kisi sirketten ayrildiginda bridge calismaya devam
/// eder.
/// </summary>
public class BridgeDeviceLogin(
    IOptions<BridgeOptions> options,
    IBridgeDisplay display,
    ILogger<BridgeDeviceLogin> logger)
{
    private readonly BridgeOptions _options = options.Value;

    /// <summary>
    /// Sunucu <c>interval</c> vermezse kullanilan aralik. RFC 8628 bu durumda
    /// 5 saniyeyi soyluyor.
    /// </summary>
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    public async Task<string?> TryLoginAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.IdentityUrl))
        {
            logger.LogError(
                "IdentityUrl tanımlı değil; kurulumu kimin onaylayacağı sorulamıyor. " +
                "{Path} dosyasına kimlik sunucusunun adresini yazın.",
                _options.ConfigurationPath);
            return null;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var endpoints = await DiscoverAsync(client, ct);
        if (endpoints is null) return null;

        var (deviceEndpoint, tokenEndpoint) = endpoints.Value;

        var authorization = await RequestCodeAsync(client, deviceEndpoint, ct);
        if (authorization is null) return null;

        Announce(authorization);

        return await PollAsync(client, tokenEndpoint, authorization, ct);
    }

    /// <summary>
    /// Uc adresleri kesif belgesinden okunuyor, elle kurulmuyor. Keycloak'in
    /// yol duzeni bize ait degil; sabit yazilirsa surum degisiminde sessizce
    /// 404 alinir.
    /// </summary>
    private async Task<(string Device, string Token)?> DiscoverAsync(
        HttpClient client, CancellationToken ct)
    {
        var url = _options.IdentityUrl.TrimEnd('/') + "/.well-known/openid-configuration";

        try
        {
            var document = await client.GetFromJsonAsync<DiscoveryDocument>(url, ct);

            if (document?.DeviceAuthorizationEndpoint is not { } device
                || document.TokenEndpoint is not { } token)
            {
                // Bu, realm'de device grant'in kapali olmasinin en yaygin
                // belirtisi. Mesaj sebebi soyluyor cunku "uc bulunamadi"
                // tek basina nereye bakilacagini anlatmiyor.
                logger.LogError(
                    "Kimlik sunucusu device flow bildirmiyor ({Url}). " +
                    "Realm'de bu istemci için device grant açık olmalı.", url);
                return null;
            }

            return (device, token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kimlik sunucusuna ulaşılamadı: {Url}", url);
            return null;
        }
    }

    private async Task<DeviceAuthorization?> RequestCodeAsync(
        HttpClient client, string endpoint, CancellationToken ct)
    {
        try
        {
            var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = _options.InstallerClientId,
                    ["scope"] = "openid",
                }), ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Kurulum kodu alınamadı ({Status}): {Body}",
                    response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return null;
            }

            return await response.Content.ReadFromJsonAsync<DeviceAuthorization>(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kurulum kodu istenirken kimlik sunucusuna ulaşılamadı.");
            return null;
        }
    }

    /// <summary>
    /// Kodu kuran kisiye gosterir.
    ///
    /// Nasil gosterilecegi kabuga birakiliyor (konsolda kutu, pencerede buyuk
    /// punto); burada karar verilen tek sey ne gosterilecegi. Gunluk satiri
    /// kabuktan bagimsiz: servis olarak kurulmus bir bridge'in kurulum kodunu
    /// gorunur kildigi tek yer orasi.
    /// </summary>
    private void Announce(DeviceAuthorization authorization)
    {
        logger.LogInformation(
            "Kurulum onayı bekleniyor. Adres: {VerificationUri} — Kod: {UserCode}",
            authorization.VerificationUri, authorization.UserCode);

        display.ShowDeviceCode(new DeviceCodePrompt(
            authorization.UserCode ?? "",
            authorization.VerificationUri ?? "",
            authorization.VerificationUriComplete));

        display.ShowStatus(BridgeStatus.AwaitingApproval);
    }

    /// <summary>
    /// Onay bekler.
    ///
    /// <c>authorization_pending</c> hata degil, "henuz onaylanmadi" demek —
    /// akisin normal hali. <c>slow_down</c> ise sunucunun cok sik sordugumuzu
    /// soylemesi; RFC 8628 araligi 5 saniye artirmayi soyluyor ve buna
    /// uyulmazsa sunucu istekleri reddetmeye baslayabiliyor.
    /// </summary>
    private async Task<string?> PollAsync(
        HttpClient client,
        string tokenEndpoint,
        DeviceAuthorization authorization,
        CancellationToken ct)
    {
        var interval = authorization.Interval > 0
            ? TimeSpan.FromSeconds(authorization.Interval)
            : DefaultPollInterval;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(
            authorization.ExpiresIn > 0 ? authorization.ExpiresIn : 600);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);

            if (DateTimeOffset.UtcNow > deadline)
            {
                logger.LogError(
                    "Kurulum kodunun süresi doldu. Servisi yeniden başlatın; yeni bir kod verilecek.");
                return null;
            }

            TokenResponse? token;
            try
            {
                var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                        ["client_id"] = _options.InstallerClientId,
                        ["device_code"] = authorization.DeviceCode ?? "",
                    }), ct);

                token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Ag kesintisi onayi iptal etmiyor: kod hâlâ gecerli, sormaya
                // devam ediliyor.
                logger.LogDebug(ex, "Onay sorulurken hata; yeniden denenecek.");
                continue;
            }

            if (token?.AccessToken is { } accessToken)
            {
                logger.LogInformation("Kurulum onaylandı.");
                return accessToken;
            }

            switch (token?.Error)
            {
                case "authorization_pending":
                    continue;

                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;

                case "expired_token":
                    logger.LogError(
                        "Kurulum kodunun süresi doldu. Servisi yeniden başlatın; " +
                        "yeni bir kod verilecek.");
                    return null;

                case "access_denied":
                    logger.LogError("Kurulum reddedildi.");
                    return null;

                default:
                    logger.LogError(
                        "Onay alınamadı: {Error} {Description}",
                        token?.Error, token?.ErrorDescription);
                    return null;
            }
        }

        return null;
    }

    private sealed class DiscoveryDocument
    {
        [JsonPropertyName("device_authorization_endpoint")]
        public string? DeviceAuthorizationEndpoint { get; set; }

        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; set; }
    }

    private sealed class DeviceAuthorization
    {
        [JsonPropertyName("device_code")] public string? DeviceCode { get; set; }
        [JsonPropertyName("user_code")] public string? UserCode { get; set; }
        [JsonPropertyName("verification_uri")] public string? VerificationUri { get; set; }

        [JsonPropertyName("verification_uri_complete")]
        public string? VerificationUriComplete { get; set; }

        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
    }
}

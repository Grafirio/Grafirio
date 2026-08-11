using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grafirio.Bridge.Contracts;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge;

/// <summary>
/// Ilk kurulum: bridge'i sirkete baglar.
///
/// Kurulum dosyasinin kendisi herkese acik durabilir — bir bridge'i sirkete
/// baglayan sey, kuran kisinin device flow ile verdigi onay. Hangi sirkete
/// baglanacagi istekten degil, o kisinin token'indaki <c>company_id</c>
/// claim'inden okunuyor: aksi halde baska bir sirkete bridge tanitmak mumkun
/// olurdu.
///
/// Onceki surumde bunun yerine panelden alinan tek kullanimlik bir token
/// vardi ve dosyaya elle yapistiriliyordu. Bkz. <see cref="BridgeDeviceLogin"/>.
/// </summary>
public class BridgeEnrollment(
    IOptions<BridgeOptions> options,
    BridgeState state,
    BridgeDeviceLogin deviceLogin,
    IBridgeDisplay display,
    ILogger<BridgeEnrollment> logger)
{
    private readonly BridgeOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Kurulum onayini device flow ile alip kaydolur. Basi olmayan kurulum —
    /// servis olarak calisan surum — bu yolu kullaniyor.
    /// </summary>
    public async Task<bool> TryEnrollAsync(CancellationToken ct)
    {
        if (!HasServerUrl()) return false;

        // Once kuran kisinin onayi. Bu token bridge'in kimligi DEGIL — yalnizca
        // asagidaki tek cagriyi yetkilendiriyor; bridge kendi kimligini o
        // cagrinin cevabinda aliyor.
        var installerToken = await deviceLogin.TryLoginAsync(ct);

        if (installerToken is null)
        {
            logger.LogError("Kurulum onayı alınamadı; kayıt yapılamıyor.");
            display.ShowStatus(
                BridgeStatus.EnrollmentFailed, "Kurulum onayı alınamadı.");
            return false;
        }

        return await EnrollWithTokenAsync(installerToken, ct);
    }

    private bool HasServerUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.ServerUrl)) return true;

        logger.LogError("ServerUrl tanımlı değil; hangi buluta kaydolunacağı bilinmiyor.");
        display.ShowStatus(
            BridgeStatus.EnrollmentFailed,
            $"ServerUrl tanımlı değil. {_options.ConfigurationPath} dosyasına " +
            "Grafirio bulut adresini yazın.");

        return false;
    }

    /// <summary>
    /// Kuran kisinin token'i zaten elde oldugunda kaydolur.
    ///
    /// Masaustu kabugu girisi tarayicida kendisi yapiyor (authorization code +
    /// PKCE) ve token'i buraya veriyor. Onayin nasil alindigi bu metodun
    /// bilmesi gereken bir sey degil: sunucu tarafindan bakildiginda ikisi de
    /// "kuran kisinin token'i" — bu ayrimi burada tutmak, iki kabuk icin iki
    /// ayri kayit yolu yazmak olurdu.
    /// </summary>
    public async Task<bool> EnrollWithTokenAsync(string installerToken, CancellationToken ct)
    {
        if (!HasServerUrl()) return false;

        display.ShowStatus(BridgeStatus.Registering);

        // IHttpClientFactory yerine tek kullanimlik istemci: kayit acilista bir
        // kez yapiliyor, soket tuketimi diye bir mesele yok. Musterinin
        // makinesine giden her paket ayri bir onay konusu.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", installerToken);

        var url = _options.ServerUrl.TrimEnd('/') + "/api/bridges/enroll";

        logger.LogInformation("Kayıt isteniyor: {Url}", url);

        try
        {
            var response = await client.PostAsJsonAsync(url, new
            {
                machineName = Environment.MachineName,
                bridgeVersion = _options.Version,
                protocolVersion = BridgeProtocol.Version,
                name = _options.Name,
            }, JsonOptions, ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // Sunucunun yazdigi sebep oldugu gibi gosteriliyor: "token'ın
                // süresi dolmuş" ile "sürümünüz uyumsuz" arasindaki fark,
                // kuran kisinin ne yapacagini belirliyor.
                logger.LogError("Kayıt reddedildi ({Status}): {Body}", response.StatusCode, body);
                display.ShowStatus(
                    BridgeStatus.EnrollmentFailed,
                    $"Kayıt reddedildi ({(int)response.StatusCode}). {Describe(body)}");
                return false;
            }

            var result = JsonSerializer.Deserialize<EnrollResponse>(body, JsonOptions);

            if (result is null || result.BridgeId == Guid.Empty
                || string.IsNullOrEmpty(result.ClientId)
                || string.IsNullOrEmpty(result.ClientSecret)
                || string.IsNullOrEmpty(result.TokenEndpoint))
            {
                logger.LogError("Kayıt cevabı okunamadı.");
                display.ShowStatus(
                    BridgeStatus.EnrollmentFailed,
                    "Kayıt cevabı okunamadı; sunucu beklenen kimliği döndürmedi.");
                return false;
            }

            state.SaveEnrollment(
                result.BridgeId,
                result.CompanyId ?? "",
                new BridgeCredentials(result.ClientId, result.ClientSecret, result.TokenEndpoint));

            logger.LogInformation(
                "Kayıt tamamlandı. Bu makinede yapılacak başka bir şey yok — kimlik " +
                "{Path} dosyasında değil, yerel durum dosyasında şifreli duruyor.",
                state.FilePath);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kayıt sırasında buluta ulaşılamadı.");

            // Adres mesajda gecmek zorunda: bu hatayi alan kisinin bakacagi
            // ilk yer yapilandirmadaki ServerUrl ve cogu zaman sorun orada.
            display.ShowStatus(
                BridgeStatus.EnrollmentFailed,
                $"{url} adresine ulaşılamadı: {ex.Message}");

            return false;
        }
    }

    /// <summary>
    /// Sunucunun yazdigi sebebi kullaniciya gosterilebilir hâle getirir.
    ///
    /// Govde JSON geliyor; ham hâliyle gostermek kullaniciyi kucuk bir
    /// ayrastiriciya cevirirdi. Beklenen bicimde degilse oldugu gibi
    /// gosteriliyor — eksik bilgi vermektense ham bilgi.
    /// </summary>
    private static string Describe(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.GetString() is { } message)
                return message;
        }
        catch (JsonException)
        {
            // JSON degilmis; asagida ham hâli gosteriliyor.
        }

        return body.Length > 300 ? body[..300] + "…" : body;
    }

    private class EnrollResponse
    {
        public Guid BridgeId { get; set; }
        public string? CompanyId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? TokenEndpoint { get; set; }
    }
}

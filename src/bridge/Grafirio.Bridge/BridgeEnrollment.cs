using System.Net.Http;
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
/// baglayan sey panelden alinan tek kullanimlik token. Token kisa omurlu ve
/// bir kez harcaniyor.
/// </summary>
public class BridgeEnrollment(
    IOptions<BridgeOptions> options,
    BridgeState state,
    ILogger<BridgeEnrollment> logger)
{
    private readonly BridgeOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<bool> TryEnrollAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.EnrollmentToken))
        {
            logger.LogError("Kayıt token'ı tanımlı değil.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.ServerUrl))
        {
            logger.LogError("ServerUrl tanımlı değil; hangi buluta kaydolunacağı bilinmiyor.");
            return false;
        }

        // IHttpClientFactory yerine tek kullanimlik istemci: kayit acilista bir
        // kez yapiliyor, soket tuketimi diye bir mesele yok. Musterinin
        // makinesine giden her paket ayri bir onay konusu.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var url = _options.ServerUrl.TrimEnd('/') + "/api/bridges/enroll";

        logger.LogInformation("Kayıt isteniyor: {Url}", url);

        try
        {
            var response = await client.PostAsJsonAsync(url, new
            {
                token = _options.EnrollmentToken,
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
                return false;
            }

            var result = JsonSerializer.Deserialize<EnrollResponse>(body, JsonOptions);

            if (result is null || result.BridgeId == Guid.Empty
                || string.IsNullOrEmpty(result.Secret))
            {
                logger.LogError("Kayıt cevabı okunamadı.");
                return false;
            }

            state.SaveEnrollment(result.BridgeId, result.Secret, result.CompanyId ?? "");

            logger.LogInformation(
                "Kayıt tamamlandı. Artık {Path} dosyasındaki EnrollmentToken alanını " +
                "silebilirsiniz; token zaten harcandı.", _options.ConfigurationPath);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kayıt sırasında buluta ulaşılamadı.");
            return false;
        }
    }

    private class EnrollResponse
    {
        public Guid BridgeId { get; set; }
        public string? Secret { get; set; }
        public string? CompanyId { get; set; }
    }
}

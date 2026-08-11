using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Grafirio.Bridge;

/// <summary>
/// Bridge'in erisim token'i.
///
/// Bridge kendi Keycloak client'i olarak <c>client_credentials</c> ile token
/// aliyor. Arkasinda oturum acmis bir insan yok ve olmasi da gerekmiyor —
/// makine kimligi tam olarak bunun icin var.
///
/// Token kisa omurlu (Keycloak varsayilani 5 dakika mertebesinde) ve burada
/// suresi dolmadan yenileniyor. Onceki surumde bridge uzun omurlu bir sirri
/// dogrudan tasiyordu; sir sizarsa suresiz gecerliydi ve iptal etmenin tek
/// yolu bizim defterimizdi.
/// </summary>
public class BridgeTokenSource(BridgeState state, ILogger<BridgeTokenSource> logger)
{
    /// <summary>
    /// Token suresinin bitmesine bu kadar kala yenileniyor. Tam bitis aninda
    /// yenilemek, saatleri birkac saniye kaymis iki makine arasinda
    /// reddedilen isteklere yol aciyor.
    /// </summary>
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string> GetAsync(CancellationToken ct = default)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt - RenewBefore)
            return _token;

        await _lock.WaitAsync(ct);
        try
        {
            // Kilidi bekleyen ikinci cagri, birincinin aldigi token'i kullansin.
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt - RenewBefore)
                return _token;

            return await FetchAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> FetchAsync(CancellationToken ct)
    {
        var credentials = state.Credentials
            ?? throw new InvalidOperationException(
                "Bridge kimliği yok; önce kayıt tamamlanmalı.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var response = await client.PostAsync(
            credentials.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
            }), ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            // Kimlik iptal edilmis olabilir: panelden "İptal Et" denince
            // Keycloak client'i kapatiliyor ve buraya 401 doner. Mesaj bunu
            // soyluyor cunku aksi halde "ağ sorunu" sanilir.
            throw new InvalidOperationException(
                $"Kimlik sunucusundan token alınamadı ({response.StatusCode}). " +
                $"Bridge iptal edilmiş olabilir. {body}");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
            ?? throw new InvalidOperationException("Token cevabı okunamadı.");

        _token = token.AccessToken;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);

        logger.LogDebug("Erişim token'ı yenilendi; {Seconds} sn geçerli.", token.ExpiresIn);

        return _token ?? throw new InvalidOperationException("Token boş döndü.");
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }
}

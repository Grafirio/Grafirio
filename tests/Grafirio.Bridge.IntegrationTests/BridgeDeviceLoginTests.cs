using System.Net.Http.Json;
using System.Text.Json;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Kurulum onayı: OAuth 2.0 Device Authorization Grant (RFC 8628), gerçek
/// Keycloak'a karşı.
///
/// Neden gerçeğine karşı: bu akışın çalışması realm'deki bir ayara bağlı —
/// istemcide device grant açık olmalı. Taklit edilmiş bir sunucuya karşı
/// ölçülseydi test geçer, kurulum sahada sessizce çalışmazdı. Ölçülen şey
/// tam olarak o ayarın açık olduğu.
///
/// Onayın kendisi tarayıcıda veriliyor ve burada taklit edilmiyor; ölçülen
/// kısım onay ÖNCESİNE kadar olan yol ve onaysız hiçbir şeyin verilmediği.
///
///     docker compose up -d keycloak
/// </summary>
public class BridgeDeviceLoginTests(KeycloakFixture keycloak)
    : IClassFixture<KeycloakFixture>
{
    /// <summary>Kurulumun onay istediği istemci — panelin kullandığının aynısı.</summary>
    private const string InstallerClientId = "grafirio-client";

    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    private static string DeviceEndpoint =>
        $"{KeycloakFixture.Authority}/protocol/openid-connect/auth/device";

    private static string TokenEndpoint =>
        $"{KeycloakFixture.Authority}/protocol/openid-connect/token";

    /// <summary>
    /// Keşif belgesi device flow'u bildiriyor.
    ///
    /// Bridge uç adreslerini elle kurmuyor, buradan okuyor: Keycloak'ın yol
    /// düzeni bize ait değil ve sabit yazılsaydı sürüm değişiminde sessizce
    /// 404 alınırdı.
    /// </summary>
    [SkippableFact]
    public async Task Kesif_belgesi_device_ucunu_bildiriyor()
    {
        Skip.IfNot(keycloak.Available, keycloak.SkipReason);

        using var client = new HttpClient();
        var document = await client.GetFromJsonAsync<JsonElement>(
            $"{KeycloakFixture.Authority}/.well-known/openid-configuration");

        Assert.True(
            document.TryGetProperty("device_authorization_endpoint", out var endpoint),
            "Keşif belgesinde device_authorization_endpoint yok.");

        Assert.Equal(DeviceEndpoint, endpoint.GetString());
    }

    /// <summary>
    /// Kuran kişiye gösterilecek kod geliyor.
    ///
    /// Bu testin asıl ölçtüğü şey realm ayarı: istemcide device grant kapalıysa
    /// Keycloak burada hata döner ve kurulum sahada hiç başlayamaz.
    /// </summary>
    [SkippableFact]
    public async Task Kurulum_kodu_veriliyor()
    {
        Skip.IfNot(keycloak.Available, keycloak.SkipReason);

        var authorization = await RequestCodeAsync();

        Assert.False(
            authorization.TryGetProperty("error", out var error),
            $"Kurulum kodu alınamadı: {error}. " +
            "Realm'de bu istemci için device grant açık olmalı.");

        Assert.False(string.IsNullOrWhiteSpace(Text(authorization, "device_code")));
        Assert.False(string.IsNullOrWhiteSpace(Text(authorization, "user_code")));

        // Kuran kişiye gösterilecek adres. Bunsuz ekranda kod var ama nereye
        // girileceği yok.
        Assert.False(string.IsNullOrWhiteSpace(Text(authorization, "verification_uri")));

        // Sorma aralığı: bridge buna uyuyor, uymazsa Keycloak slow_down döner.
        Assert.True(authorization.GetProperty("interval").GetInt32() > 0);
    }

    /// <summary>
    /// <b>Onaylanmadan token verilmiyor.</b>
    ///
    /// Akışın güvenlik iddiası bu: kodu ele geçirmek yetmiyor, bir insanın
    /// tarayıcıda onaylaması gerekiyor. <c>authorization_pending</c> hata
    /// değil, "henüz onaylanmadı" demek — bridge bunu bekleme olarak okuyor.
    /// </summary>
    [SkippableFact]
    public async Task Onaylanmadan_token_verilmiyor()
    {
        Skip.IfNot(keycloak.Available, keycloak.SkipReason);

        var authorization = await RequestCodeAsync();
        Skip.If(authorization.TryGetProperty("error", out _), "Kurulum kodu alınamadı.");

        var response = await PollAsync(Text(authorization, "device_code"));

        Assert.False(
            response.TryGetProperty("access_token", out _),
            "Onaylanmamış bir kurulum için token verildi.");

        Assert.Equal("authorization_pending", Text(response, "error"));
    }

    /// <summary>Uydurma bir kod token'a çevrilemiyor.</summary>
    [SkippableFact]
    public async Task Uydurma_kod_token_a_cevrilemiyor()
    {
        Skip.IfNot(keycloak.Available, keycloak.SkipReason);

        var response = await PollAsync("uydurma-device-code");

        Assert.False(response.TryGetProperty("access_token", out _));
        Assert.NotEqual("authorization_pending", Text(response, "error"));
    }

    private static async Task<JsonElement> RequestCodeAsync()
    {
        using var client = new HttpClient();

        var response = await client.PostAsync(DeviceEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = InstallerClientId,
                ["scope"] = "openid",
            }));

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> PollAsync(string deviceCode)
    {
        using var client = new HttpClient();

        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = DeviceGrant,
                ["client_id"] = InstallerClientId,
                ["device_code"] = deviceCode,
            }));

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";
}

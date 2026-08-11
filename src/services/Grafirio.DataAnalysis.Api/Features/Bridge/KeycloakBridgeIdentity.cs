using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grafirio.Bridge.Contracts;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Bridge kimliklerini Keycloak'ta acar ve kapatir.
///
/// Neden Keycloak: onceki surumde kimlik dogrulama elle yaziliyordu — kendi
/// token'imiz, kendi SHA256 ozetimiz, kendi sabit sureli karsilastirmamiz.
/// Yazdigimiz sey aslinda OAuth Dynamic Client Registration'in elle yapilmis
/// haliydi ve auth sunucusu zaten kuruluydu. Token suresi, anahtar rotasyonu
/// ve merkezi iptal, kendi surumumuzde hic yoktu.
///
/// Model: <b>her bridge bir Keycloak client'i</b>.
///   * <c>client_credentials</c> ile token aliyor; arkasinda oturum acmis bir
///     insan yok, olmasi da gerekmiyor.
///   * Token'da <c>bridge_id</c> ve <c>company_id</c> claim'leri var; sunucu
///     tarafinda ayri bir esleme tablosuna bakmaya gerek kalmiyor.
///   * Iptal etmek client'i silmek demek — bridge bir sonraki token
///     yenilemesinde disarida kaliyor.
/// </summary>
public class KeycloakBridgeIdentity(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<KeycloakBridgeIdentity> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Keycloak yonetim adresi, ornegin http://keycloak:8080</summary>
    private string BaseUrl =>
        (configuration["Keycloak:BaseUrl"]
         ?? Environment.GetEnvironmentVariable("KEYCLOAK__BASEURL")
         ?? throw new InvalidOperationException(
             "Keycloak:BaseUrl tanımlı değil; bridge kimliği açılamaz.")).TrimEnd('/');

    private string Realm =>
        configuration["Keycloak:Realm"]
        ?? Environment.GetEnvironmentVariable("KEYCLOAK__REALM")
        ?? "grafirio";

    /// <summary>
    /// Bridge token'ina eklenecek audience — API'nin dogruladigi deger.
    /// Eklenmezse token yalnizca "account" audience'iyla gelir ve API onu
    /// reddeder; hata da "yetkisiz" der, sebebini soylemez.
    /// </summary>
    private string Audience =>
        configuration["IdentityOption:Audience"] ?? "gateway.api";

    /// <summary>
    /// Client acmaya yetkili hesap. Uretimde bu, realm uzerinde yalnizca
    /// <c>manage-clients</c> yetkisi olan bir servis hesabi olmali —
    /// realm yoneticisi degil.
    /// </summary>
    private (string ClientId, string Secret)? ServiceAccount
    {
        get
        {
            var id = configuration["Keycloak:AdminClientId"]
                     ?? Environment.GetEnvironmentVariable("KEYCLOAK__ADMINCLIENTID");
            var secret = configuration["Keycloak:AdminClientSecret"]
                         ?? Environment.GetEnvironmentVariable("KEYCLOAK__ADMINCLIENTSECRET");

            return id is not null && secret is not null ? (id, secret) : null;
        }
    }

    /// <summary>
    /// Yerel gelistirme icin kullanici adi/sifre ile master realm yoneticisi.
    /// Uretimde <see cref="ServiceAccount"/> kullanilmali; ikisi de yoksa
    /// acilista degil, ilk kayitta acik bir hata veriliyor.
    /// </summary>
    private (string User, string Password)? AdminUser
    {
        get
        {
            var user = configuration["Keycloak:AdminUsername"]
                       ?? Environment.GetEnvironmentVariable("KEYCLOAK__ADMINUSERNAME");
            var password = configuration["Keycloak:AdminPassword"]
                           ?? Environment.GetEnvironmentVariable("KEYCLOAK__ADMINPASSWORD");

            return user is not null && password is not null ? (user, password) : null;
        }
    }

    /// <summary>
    /// Bridge icin bir Keycloak client'i acar ve kimlik bilgisini doner.
    /// Sir yalnizca burada, bir kez goruluyor; Keycloak onu kendi saklıyor.
    /// </summary>
    public async Task<BridgeCredentials> CreateAsync(
        Guid bridgeId, string companyId, string name, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client, ct));

        var clientId = ClientIdFor(bridgeId);

        var representation = new
        {
            clientId,
            name = $"Grafirio Bridge — {name}",
            enabled = true,
            publicClient = false,
            serviceAccountsEnabled = true,
            // Bridge bir makine: tarayici akislarina ihtiyaci yok ve acik
            // birakilan her akis fazladan bir saldiri yuzeyi.
            standardFlowEnabled = false,
            directAccessGrantsEnabled = false,
            protocolMappers = new object[]
            {
                HardcodedClaim("bridge_id", bridgeId.ToString()),
                HardcodedClaim("company_id", companyId),
                new
                {
                    name = "audience",
                    protocol = "openid-connect",
                    protocolMapper = "oidc-audience-mapper",
                    config = new Dictionary<string, string>
                    {
                        ["included.client.audience"] = Audience,
                        ["access.token.claim"] = "true",
                    },
                },
            },
        };

        var create = await client.PostAsJsonAsync(
            $"{BaseUrl}/admin/realms/{Realm}/clients", representation, JsonOptions, ct);

        if (!create.IsSuccessStatusCode)
        {
            var body = await create.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Keycloak'ta bridge kimliği açılamadı ({create.StatusCode}): {body}");
        }

        var internalId = await FindInternalIdAsync(client, clientId, ct)
            ?? throw new InvalidOperationException("Açılan client bulunamadı.");

        var secretResponse = await client.GetFromJsonAsync<SecretResponse>(
            $"{BaseUrl}/admin/realms/{Realm}/clients/{internalId}/client-secret", ct);

        logger.LogInformation(
            "Bridge kimliği Keycloak'ta açıldı. Bridge: {BridgeId}, client: {ClientId}",
            bridgeId, clientId);

        return new BridgeCredentials(
            clientId,
            secretResponse?.Value ?? throw new InvalidOperationException("Client sırrı alınamadı."),
            $"{BaseUrl}/realms/{Realm}/protocol/openid-connect/token");
    }

    /// <summary>
    /// Bridge kimligini kapatir. Silme yerine devre disi birakiliyor: kaydin
    /// kalmasi, "bu bridge ne zaman iptal edildi" sorusunu cevaplanabilir
    /// tutuyor.
    /// </summary>
    public async Task<bool> DisableAsync(Guid bridgeId, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(client, ct));

        var internalId = await FindInternalIdAsync(client, ClientIdFor(bridgeId), ct);
        if (internalId is null) return false;

        var response = await client.PutAsJsonAsync(
            $"{BaseUrl}/admin/realms/{Realm}/clients/{internalId}",
            new { enabled = false }, JsonOptions, ct);

        logger.LogInformation("Bridge kimliği devre dışı bırakıldı: {BridgeId}", bridgeId);
        return response.IsSuccessStatusCode;
    }

    public static string ClientIdFor(Guid bridgeId) => $"bridge-{bridgeId}";

    private static object HardcodedClaim(string name, string value) => new
    {
        name,
        protocol = "openid-connect",
        protocolMapper = "oidc-hardcoded-claim-mapper",
        config = new Dictionary<string, string>
        {
            ["claim.name"] = name,
            ["claim.value"] = value,
            ["jsonType.label"] = "String",
            ["access.token.claim"] = "true",
        },
    };

    private async Task<string?> FindInternalIdAsync(
        HttpClient client, string clientId, CancellationToken ct)
    {
        var found = await client.GetFromJsonAsync<List<ClientResponse>>(
            $"{BaseUrl}/admin/realms/{Realm}/clients?clientId={Uri.EscapeDataString(clientId)}", ct);

        return found?.FirstOrDefault()?.Id;
    }

    private async Task<string> AdminTokenAsync(HttpClient client, CancellationToken ct)
    {
        // Servis hesabi tercih ediliyor; yoksa yerel gelistirme icin yonetici
        // kullanicisina dusuluyor. Ikisi de yoksa sebebi acik bir hata.
        var (url, form) = ServiceAccount is { } service
            ? ($"{BaseUrl}/realms/{Realm}/protocol/openid-connect/token",
               new Dictionary<string, string>
               {
                   ["grant_type"] = "client_credentials",
                   ["client_id"] = service.ClientId,
                   ["client_secret"] = service.Secret,
               })
            : AdminUser is { } admin
                ? ($"{BaseUrl}/realms/master/protocol/openid-connect/token",
                   new Dictionary<string, string>
                   {
                       ["grant_type"] = "password",
                       ["client_id"] = "admin-cli",
                       ["username"] = admin.User,
                       ["password"] = admin.Password,
                   })
                : throw new InvalidOperationException(
                    "Keycloak yönetim kimliği tanımlı değil (Keycloak:AdminClientId/Secret " +
                    "ya da Keycloak:AdminUsername/Password). Bridge kimliği açılamaz.");

        var response = await client.PostAsync(url, new FormUrlEncodedContent(form), ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Keycloak yönetim token'ı alınamadı ({response.StatusCode}).");

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return token?.AccessToken ?? throw new InvalidOperationException("Token okunamadı.");
    }

    private sealed class ClientResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
    }

    private sealed class SecretResponse
    {
        [JsonPropertyName("value")] public string? Value { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }
}


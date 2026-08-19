using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Grafirio.Identity.Api.Features.Users.Directory;

/// <summary>
/// Kullanıcı kimliğinden ada ve e-postaya çeviren okuyucu.
///
/// Kullanıcı listeleri Keycloak kimliğini (GUID) gösteriyordu: yetki kayıtları
/// <see cref="CompanyMembership.KeycloakUserId"/> tutuyor, ad ve e-posta ise
/// Keycloak'ta duruyor ve paylaşılan <c>IKeycloakUserService</c>'in okuma
/// metodu yok — yalnızca kullanıcı açıp şirkete bağlıyor.
///
/// Ad bilgisi yetki kaydına kopyalanmıyor. Kopya iki nedenle yanlış olurdu:
/// mevcut kayıtlarda böyle bir alan yok, yani liste bugünkü kullanıcılar için
/// GUID göstermeye devam ederdi; üstelik kişi adını ya da e-postasını
/// değiştirdiğinde panel eskimiş bilgiyi gösterirdi. Kimliğin sahibi Keycloak,
/// kaynak oradan okunuyor.
///
/// Hata durumunda boş dönüyor, patlamıyor: adı okuyamamak kullanıcı listesini
/// hiç gösterememek için yeterli bir sebep değil. Çağıran taraf GUID'e düşer.
/// </summary>
public class KeycloakUserDirectory(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<KeycloakUserDirectory> logger)
{
    /// <summary>
    /// Ad/e-posta sık değişmiyor ama sonsuza kadar da saklanmamalı: kullanıcı
    /// adını düzelttiğinde panelin bunu birkaç dakika içinde görmesi yeterli.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// Yönetici token'ı ömrü boyunca değil, kısa süre saklanıyor; süresi dolan
    /// token'la yapılan çağrı 401 dönerdi.
    private static readonly TimeSpan TokenCacheDuration = TimeSpan.FromSeconds(50);

    private const string TokenCacheKey = "keycloak-admin-token";

    /// <summary>
    /// Verilen kimlikler için ad/e-posta. Bulunamayanlar sözlükte hiç yer
    /// almıyor — çağıran taraf eksik olanı GUID olarak gösterir.
    /// </summary>
    public async Task<Dictionary<string, KeycloakUserInfo>> LookupAsync(
        IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        var result = new Dictionary<string, KeycloakUserInfo>();
        if (ids.Count == 0) return result;

        var missing = new List<string>();

        foreach (var id in ids)
        {
            if (cache.TryGetValue(CacheKey(id), out KeycloakUserInfo? cached) && cached is not null)
            {
                result[id] = cached;
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count == 0) return result;

        var token = await GetAdminTokenAsync(ct);
        if (token is null) return result;

        var address = AdminBaseUrl();
        var realm = configuration["KeycloakAdmin:Realm"];

        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(realm)) return result;

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Kimlik başına bir istek. Keycloak'ta "şu kimliklerin hepsini getir"
        // diye bir uç yok; realm'in tamamını çekip süzmek ise kiracı sayısı
        // arttıkça listeyle ilgisiz binlerce kaydı taşımak olurdu. Liste
        // ekranları sayfalı ve kullanıcı sayısı lisansla sınırlı, dolayısıyla
        // istek sayısı da sınırlı.
        foreach (var id in missing)
        {
            try
            {
                var response = await client.GetAsync(
                    $"{address}/admin/realms/{realm}/users/{Uri.EscapeDataString(id)}", ct);

                if (!response.IsSuccessStatusCode)
                {
                    // Silinmiş kullanıcının yetki kaydı hâlâ duruyor olabilir;
                    // bu bir hata değil, denetim izinin doğal sonucu.
                    logger.LogDebug("Keycloak kullanıcısı okunamadı: {UserId} → {Status}",
                        id, response.StatusCode);
                    continue;
                }

                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(ct));

                var info = Read(document.RootElement);
                cache.Set(CacheKey(id), info, CacheDuration);
                result[id] = info;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Keycloak kullanıcı okuması başarısız: {UserId}", id);
            }
        }

        return result;
    }

    private static KeycloakUserInfo Read(JsonElement user)
    {
        var firstName = Text(user, "firstName");
        var lastName = Text(user, "lastName");
        var email = Text(user, "email");
        var username = Text(user, "username");

        // Görünen ad için sıra: ad soyad → e-posta → kullanıcı adı. Hepsi boş
        // olabilir (Keycloak yalnızca kullanıcı adını zorunlu tutuyor), o
        // durumda null dönüyoruz ve panel kimliği gösteriyor.
        var full = string.Join(' ', new[] { firstName, lastName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        var display = !string.IsNullOrWhiteSpace(full) ? full
            : !string.IsNullOrWhiteSpace(email) ? email
            : username;

        return new KeycloakUserInfo(display, email, firstName, lastName, username);
    }

    private static string? Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Admin API'nin kok adresi.
    ///
    /// IdentityOption.Address iki uyumsuz amaca hizmet ediyor: JWT authority'si
    /// olarak realm URL'i olmasi gerekiyor, Admin API tabani olarak ise realm'siz
    /// kok. Ham okunduğunda uretimde ".../realms/x/admin/realms/x/users" gibi bir
    /// adres olusuyor, Keycloak 404 donuyor ve liste herkesi GUID gosteriyordu.
    ///
    /// Cozum paylasilan KeycloakUserService ile ayni: KeycloakAdmin:AdminAddress
    /// verilmisse o, verilmemisse adresteki "/realms/..." eki atilarak kok.
    /// Ikisi ayrisirsa kullanici acma calisip ad okuma calismazdi.
    /// </summary>
    private string? AdminBaseUrl()
    {
        var adminAddress = configuration["KeycloakAdmin:AdminAddress"];

        if (!string.IsNullOrWhiteSpace(adminAddress)) return adminAddress.TrimEnd('/');

        var address = configuration["IdentityOption:Address"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(address)) return null;

        var realmsIndex = address.IndexOf("/realms/", StringComparison.OrdinalIgnoreCase);

        return realmsIndex > 0 ? address[..realmsIndex] : address;
    }

    private static string CacheKey(string userId) => $"keycloak-user:{userId}";

    /// <summary>
    /// Yönetici token'ı. Kullanıcı açma akışıyla aynı kimlik bilgileri
    /// (<c>KeycloakAdmin</c>) kullanılıyor; parola akışı master realm'in
    /// <c>admin-cli</c> istemcisinden geçiyor.
    /// </summary>
    private async Task<string?> GetAdminTokenAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null) return cached;

        var address = AdminBaseUrl();
        var username = configuration["KeycloakAdmin:Username"];
        var password = configuration["KeycloakAdmin:Password"];

        if (string.IsNullOrWhiteSpace(address) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "KeycloakAdmin yapılandırması eksik; kullanıcı adları okunamayacak.");
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient();

            var response = await client.PostAsync(
                $"{address}/realms/master/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "admin-cli",
                    ["username"] = username,
                    ["password"] = password
                }), ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Keycloak yönetici token'ı alınamadı: {Status}", response.StatusCode);
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            if (!document.RootElement.TryGetProperty("access_token", out var token)) return null;

            var value = token.GetString();
            if (!string.IsNullOrWhiteSpace(value)) cache.Set(TokenCacheKey, value, TokenCacheDuration);

            return value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Keycloak yönetici token'ı alınamadı.");
            return null;
        }
    }
}

/// <param name="DisplayName">Panelde gösterilecek ad; hiçbiri yoksa null.</param>
public record KeycloakUserInfo(
    string? DisplayName,
    string? Email,
    string? FirstName,
    string? LastName,
    string? Username);

using System.Net.Http.Json;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Gerçek Keycloak. Bridge kimliği artık orada duruyor, dolayısıyla kimlik
/// doğrulamayı ölçmek için taklit değil gerçeği gerekiyor.
///
///     docker compose up -d keycloak
/// </summary>
public class KeycloakFixture : IAsyncLifetime
{
    public const string BaseUrl = "http://localhost:8080";
    public const string Realm = "grafirio";
    public const string Audience = "gateway.api";

    public static string Authority => $"{BaseUrl}/realms/{Realm}";

    public bool Available { get; private set; }

    public string SkipReason { get; private set; } = "";

    public string AdminUsername { get; private set; } = "";

    public string AdminPassword { get; private set; } = "";

    public async Task InitializeAsync()
    {
        AdminUsername = DotEnv.Read("KEYCLOAK_ADMIN") ?? "";
        AdminPassword = DotEnv.Read("KEYCLOAK_ADMIN_PASSWORD") ?? "";

        if (AdminUsername is "" || AdminPassword is "")
        {
            SkipReason = "KEYCLOAK_ADMIN/KEYCLOAK_ADMIN_PASSWORD okunamadı.";
            return;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // Realm'in açık OIDC yapılandırması: hem ayakta olduğunu hem de
            // doğru realm'e baktığımızı bir arada doğruluyor.
            var discovery = await client.GetAsync(
                $"{Authority}/.well-known/openid-configuration");

            Available = discovery.IsSuccessStatusCode;

            if (!Available)
                SkipReason = $"Keycloak realm '{Realm}' yanıt vermedi ({discovery.StatusCode}).";
        }
        catch (Exception ex)
        {
            SkipReason = $"Keycloak'a ulaşılamadı: {ex.Message}";
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

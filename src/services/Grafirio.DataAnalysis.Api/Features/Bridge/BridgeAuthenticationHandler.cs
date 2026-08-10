using System.Text.Encodings.Web;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Bridge'lerin kendini tanittigi sema.
///
/// Kullanici token'i (Keycloak) burada ise yaramaz: bridge bir insan degil,
/// musterinin sunucusunda kosan bir servis ve arkasinda oturum acmis kimse yok.
/// Kayit sirasinda aldigi uzun omurlu sirri kullaniyor.
///
/// Baslik bicimi: <c>Authorization: Bridge {bridgeId}:{secret}</c>
///
/// Tarayici WebSocket'i ozel baslik gonderemez ama bridge bir .NET istemcisi;
/// baslik yolu bu yuzden secildi. Sirri sorgu dizesine koymak, her ara
/// sunucunun erisim gunlugune yazmak olurdu.
/// </summary>
public class BridgeAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    BridgeStore store)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(header))
            return AuthenticateResult.NoResult();

        if (!header.StartsWith(BridgeAuthentication.Scheme + " ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var credential = header[(BridgeAuthentication.Scheme.Length + 1)..].Trim();
        var separator = credential.IndexOf(':');

        if (separator <= 0)
            return AuthenticateResult.Fail("Bridge kimlik bilgisi biçimsiz.");

        if (!Guid.TryParse(credential[..separator], out var bridgeId))
            return AuthenticateResult.Fail("Bridge kimliği geçersiz.");

        var bridge = await store.AuthenticateAsync(bridgeId, credential[(separator + 1)..]);

        if (bridge is null)
        {
            // Hangi kismin yanlis oldugu (kimlik mi sir mi) disari verilmiyor.
            Logger.LogWarning("Bridge kimlik doğrulaması başarısız. Id: {BridgeId}", bridgeId);
            return AuthenticateResult.Fail("Bridge kimlik doğrulaması başarısız.");
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(bridge.ToPrincipal(), BridgeAuthentication.Scheme));
    }
}

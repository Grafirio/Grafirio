using System.Net.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Grafirio.Bridge.Desktop.Authentication;

public static class DesktopAuthenticationServiceCollectionExtensions
{
    private const string HttpClientKey = "Grafirio.Desktop.Authentication";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddDesktopAuthentication(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<DesktopIdentityAuthority>();
        services.AddKeyedSingleton<HttpClient>(HttpClientKey, (_, _) =>
            new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            })
            {
                Timeout = RequestTimeout
            });
        services.TryAddSingleton(provider => new DesktopOAuthClient(
            provider.GetRequiredKeyedService<HttpClient>(HttpClientKey),
            provider.GetRequiredService<DesktopIdentityAuthority>(),
            provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<BrowserLogin>();
        services.TryAddSingleton<IBrowserLogin>(provider => provider.GetRequiredService<BrowserLogin>());
        services.TryAddSingleton<IDesktopSessionStore, DpapiDesktopSessionStore>();
        services.TryAddSingleton<IDesktopSessionManager, DesktopSessionManager>();
        return services;
    }
}
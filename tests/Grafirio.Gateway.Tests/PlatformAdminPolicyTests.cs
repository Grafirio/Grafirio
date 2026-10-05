using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Grafirio.Gateway.Tests;

/// <summary>
/// Benchmark dashboard'unun kimlik dogrulamasi yok; disari yalnizca bu politikayla
/// aciliyor. Politika yanlis kurulursa ya herkese acilir ya da (semasizsa)
/// herkese sessizce 401 doner.
/// </summary>
public sealed class PlatformAdminPolicyTests
{
    private static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim("business_roles", r)), authenticationType: "test"));

    [Fact]
    public void Rol_dizi_ya_da_virgullu_metin_olarak_taninıyor()
    {
        Assert.True(global::Program.IsPlatformAdmin(User("COMPANY_ADMIN", "PLATFORM_ADMIN")));
        Assert.True(global::Program.IsPlatformAdmin(User("COMPANY_ADMIN,PLATFORM_ADMIN")));
        Assert.False(global::Program.IsPlatformAdmin(User("COMPANY_ADMIN")));
        Assert.False(global::Program.IsPlatformAdmin(User("platform_admin")));
        Assert.False(global::Program.IsPlatformAdmin(User()));
    }

    [Fact]
    public async Task Politika_semali_ve_yalnizca_platform_yoneticisini_geciriyor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(global::Program.AddPlatformAdminPolicy);
        await using var provider = services.BuildServiceProvider();

        var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(global::Program.PlatformAdminPolicy);
        Assert.NotNull(policy);
        Assert.NotEmpty(policy!.AuthenticationSchemes);

        var authorization = provider.GetRequiredService<IAuthorizationService>();
        Assert.True((await authorization.AuthorizeAsync(User("PLATFORM_ADMIN"), policy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(User("COMPANY_ADMIN"), policy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), policy)).Succeeded);
    }
}

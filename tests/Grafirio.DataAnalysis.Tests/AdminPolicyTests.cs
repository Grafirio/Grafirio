using Grafirio.Shared.Identity.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Admin ucunun kullandigi "Password" politikasi DataAnalysis'te kayitli mi ve
/// bir kimlik dogrulama semasi tasiyor mu.
///
/// Sebep: paylasilan kurulum varsayilan sema vermiyor; semasiz bir politika
/// her istegi sessizce 401 ile reddediyor ve bu ancak canlida gorunuyor.
/// </summary>
public class AdminPolicyTests
{
    [Fact]
    public async Task Password_politikasi_kayitli_ve_semali()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentityOption:Address"] = "http://keycloak.test/realms/grafirio",
            ["IdentityOption:Issuer"] = "http://keycloak.test/realms/grafirio",
            ["IdentityOption:Audience"] = "gateway.api"
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAuthenticationAndAuthorizationExt(configuration);

        await using var provider = services.BuildServiceProvider();
        var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync("Password");

        Assert.NotNull(policy);
        Assert.NotEmpty(policy!.AuthenticationSchemes);
        Assert.Contains(policy.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }
}

using Grafirio.Bridge.Desktop.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Grafirio.Bridge.Desktop.Tests.Authentication;

public sealed class DesktopAuthenticationRegistrationTests
{
    [Fact]
    public void RegistersSingletonManagerWithoutWindowDependency()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<BridgeOptions>(options =>
        {
            options.IdentityUrl = AuthenticationTestContext.Issuer;
            options.InstallerClientId = AuthenticationTestContext.ClientId;
        });
        services.AddDesktopAuthentication();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var manager = provider.GetRequiredService<IDesktopSessionManager>();
        Assert.Same(manager, provider.GetRequiredService<IDesktopSessionManager>());
        Assert.Same(provider.GetRequiredService<BrowserLogin>(), provider.GetRequiredService<IBrowserLogin>());
        Assert.IsType<DpapiDesktopSessionStore>(provider.GetRequiredService<IDesktopSessionStore>());
    }
}
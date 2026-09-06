using Grafirio.Bridge.Desktop.Cloud;
using Grafirio.Bridge.Desktop.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Grafirio.Bridge.Desktop.Tests.Cloud;

public sealed class DesktopBridgeControllerTests
{
    [Fact]
    public async Task RegistrationIsSingletonWithoutWindowDependency()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDesktopSessionManager>(new ContextSessionManager());
        services.AddDesktopBridge();
        services.AddDesktopBridge();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var controller = provider.GetRequiredService<IDesktopBridgeController>();
        Assert.Same(controller, provider.GetRequiredService<IDesktopBridgeController>());
        Assert.Single(provider.GetServices<IDesktopBridgeController>());
    }

    [Fact]
    public async Task InvalidSessionReportsSafeFailureAndStopIsIdempotent()
    {
        await using var controller = CreateController();
        var statuses = new List<BridgeStatus>();
        var details = new List<string?>();
        controller.Changed += (status, detail) => { statuses.Add(status); details.Add(detail); };

        var failure = await Assert.ThrowsAsync<DesktopBridgeException>(() =>
            controller.ConnectAsync(new UserSession("secret-invalid-token", null, null), CancellationToken.None));
        Assert.Contains(BridgeStatus.Stopped, statuses);
        Assert.Equal(BridgeStatus.Disconnected, statuses[^1]);
        Assert.DoesNotContain("secret-invalid-token", failure.Message);
        Assert.DoesNotContain(details, detail => detail?.Contains("secret-invalid-token") == true);
        await controller.StopAsync(CancellationToken.None);
        await controller.StopAsync(CancellationToken.None);
        Assert.Equal(BridgeStatus.Stopped, statuses[^1]);
    }

    [Fact]
    public async Task CancellationAndDisposalAreRespected()
    {
        var controller = CreateController();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.ConnectAsync(new UserSession("unused", null, null), cancellation.Token));
        await controller.DisposeAsync();
        await controller.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            controller.ConnectAsync(new UserSession("unused", null, null), CancellationToken.None));
        await controller.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FailingStatusSubscriberDoesNotBlockShutdown()
    {
        await using var controller = CreateController();
        controller.Changed += (_, _) => throw new InvalidOperationException("subscriber failure");
        await controller.StopAsync(CancellationToken.None);
    }

    private static DesktopBridgeController CreateController() =>
        new(new ContextSessionManager(), Options.Create(new BridgeOptions()), NullLoggerFactory.Instance);
}
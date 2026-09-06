using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Grafirio.Bridge.Desktop.Cloud;

public static class DesktopBridgeServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopBridge(this IServiceCollection services)
    {
        services.AddOptions<BridgeOptions>();
        services.TryAddSingleton<IDesktopBridgeController, DesktopBridgeController>();
        return services;
    }
}
using Grafirio.Bridge.Desktop.LocalWorkspace.Data;
using Grafirio.Bridge.Desktop.LocalWorkspace.Export;
using Grafirio.Bridge.Desktop.LocalWorkspace.Messaging;
using Grafirio.Bridge.Desktop.LocalWorkspace.Services;
using Grafirio.Bridge.Desktop.LocalWorkspace.Storage;

namespace Grafirio.Bridge.Desktop.LocalWorkspace;

public static class LocalWorkspaceRegistration
{
    public static IServiceCollection AddLocalWorkspace(this IServiceCollection services)
    {
        services.AddSingleton<ILocalWorkspaceStore, DpapiLocalWorkspaceStore>();
        services.AddSingleton<ILocalDatabase, LocalDatabase>();
        services.AddSingleton<ILocalWorkspaceService, LocalWorkspaceService>();
        services.AddTransient<ILocalCsvExporter, LocalCsvExporter>();
        services.AddTransient<WorkspaceMessageDispatcher>();
        services.AddTransient<LocalWorkspaceView>();
        return services;
    }
}
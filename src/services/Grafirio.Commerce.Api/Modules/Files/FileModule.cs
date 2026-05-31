using Asp.Versioning.Builder;
using Microsoft.Extensions.FileProviders;

namespace Grafirio.Commerce.Api.Modules.Files;

public static class FileModule
{
    public static IServiceCollection AddFileModule(this IServiceCollection services)
    {
        var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        Directory.CreateDirectory(wwwroot);
        services.AddSingleton<IFileProvider>(new PhysicalFileProvider(wwwroot));

        services.AddScoped<FileService>();
        return services;
    }

    public static WebApplication MapFileEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        FileEndpoints.Map(app, vs);
        return app;
    }
}

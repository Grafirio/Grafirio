using Asp.Versioning.Builder;
using Microsoft.Extensions.Options;

namespace Grafirio.Commerce.Api.Modules.Catalog;

public record CatalogMongoOption
{
    public string DatabaseName { get; init; } = default!;
}

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<CatalogMongoOption>()
            .BindConfiguration(nameof(CatalogMongoOption))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<CatalogDbContext>(sp =>
        {
            var opt    = sp.GetRequiredService<IOptions<CatalogMongoOption>>().Value;
            var client = sp.GetRequiredService<IMongoClient>();
            return CatalogDbContext.Create(client.GetDatabase(opt.DatabaseName));
        });

        services.AddScoped<CatalogService>();
        return services;
    }

    public static WebApplication MapCatalogEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        CatalogEndpoints.Map(app, vs);
        return app;
    }
}

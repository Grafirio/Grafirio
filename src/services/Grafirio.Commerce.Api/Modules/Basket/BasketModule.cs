using Asp.Versioning.Builder;

namespace Grafirio.Commerce.Api.Modules.Basket;

public static class BasketModule
{
    public static IServiceCollection AddBasketModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddStackExchangeRedisCache(o =>
            o.Configuration = config.GetConnectionString("Redis"));

        services.AddScoped<BasketService>();
        return services;
    }

    public static WebApplication MapBasketEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        BasketEndpoints.Map(app, vs);
        return app;
    }
}

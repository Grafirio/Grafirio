using Asp.Versioning.Builder;
using Microsoft.Extensions.Options;

namespace Grafirio.Commerce.Api.Modules.Discount;

public record DiscountMongoOption
{
    public string DatabaseName { get; init; } = default!;
}

public static class DiscountModule
{
    public static IServiceCollection AddDiscountModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<DiscountMongoOption>()
            .BindConfiguration(nameof(DiscountMongoOption))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<DiscountDbContext>(sp =>
        {
            var opt    = sp.GetRequiredService<IOptions<DiscountMongoOption>>().Value;
            var client = sp.GetRequiredService<IMongoClient>();
            return DiscountDbContext.Create(client.GetDatabase(opt.DatabaseName));
        });

        services.AddScoped<DiscountService>();
        return services;
    }

    public static WebApplication MapDiscountEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        DiscountEndpoints.Map(app, vs);
        return app;
    }
}

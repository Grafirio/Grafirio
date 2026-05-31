using Asp.Versioning.Builder;

namespace Grafirio.Commerce.Api.Modules.Orders;

public static class OrderModule
{
    public static IServiceCollection AddOrderModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<OrderDbContext>(opt =>
            opt.UseSqlServer(config.GetConnectionString("SqlServer")));

        services.AddScoped<OrderService>();
        return services;
    }

    public static WebApplication MapOrderEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        OrderEndpoints.Map(app, vs);
        return app;
    }
}

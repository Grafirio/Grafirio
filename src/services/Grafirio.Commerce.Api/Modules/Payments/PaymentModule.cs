using Asp.Versioning.Builder;

namespace Grafirio.Commerce.Api.Modules.Payments;

public static class PaymentModule
{
    public static IServiceCollection AddPaymentModule(this IServiceCollection services)
    {
        services.AddDbContext<PaymentDbContext>(opt =>
            opt.UseInMemoryDatabase("payments-db"));

        services.AddScoped<PaymentService>();
        return services;
    }

    public static WebApplication MapPaymentEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        PaymentEndpoints.Map(app, vs);
        return app;
    }
}

using Grafirio.Shared.MassTransit.Options;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Grafirio.Shared.MassTransit.Extensions;

public static class MassTransitExt
{
    /// <summary>
    /// RabbitMQ ile MassTransit'i global olarak yapılandırır
    /// </summary>
    public static IServiceCollection AddGrafiiroMassTransit(
        this IServiceCollection services, 
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        // RabbitMQ ayarlarını yükle
        var rabbitMqOptions = configuration.GetSection(RabbitMqOptions.Key).Get<RabbitMqOptions>();
        
        if (rabbitMqOptions is null)
        {
            throw new InvalidOperationException($"RabbitMq configuration section '{RabbitMqOptions.Key}' is missing.");
        }

        services.AddMassTransit(x =>
        {
            // Consumer'lar için özel konfigürasyon (opsiyonel)
            configure?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitMqOptions.Host, rabbitMqOptions.Port, "/", h =>
                {
                    h.Username(rabbitMqOptions.Username);
                    h.Password(rabbitMqOptions.Password);
                });

                // Message durability - mesajlar persist edilsin
                cfg.Durable = true;

                // Retry policy
                cfg.UseMessageRetry(r => r.Intervals(
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(30)
                ));

                // Endpoint yapılandırması
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}

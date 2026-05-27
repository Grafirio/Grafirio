using Grafirio.Shared.MassTransit.Options;
using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Grafirio.Shared.MassTransit.Extensions;

public static class MassTransitExt
{
    /// <summary>
    /// RabbitMQ ile MassTransit'i global olarak yapılandırır.
    /// </summary>
    /// <param name="configure">Consumer kayıtları için.</param>
    /// <param name="configureTopology">Exchange/routing key topolojisi için (opsiyonel).</param>
    public static IServiceCollection AddGrafiiroMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configure = null,
        Action<IRabbitMqBusFactoryConfigurator>? configureTopology = null)
    {
        var rabbitMqOptions = configuration.GetSection(RabbitMqOptions.Key).Get<RabbitMqOptions>();

        if (rabbitMqOptions is null)
            throw new InvalidOperationException($"RabbitMq configuration section '{RabbitMqOptions.Key}' is missing.");

        services.AddMassTransit(x =>
        {
            configure?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitMqOptions.Host, rabbitMqOptions.Port, "/", h =>
                {
                    h.Username(rabbitMqOptions.Username);
                    h.Password(rabbitMqOptions.Password);
                });

                cfg.Durable = true;

                cfg.UseMessageRetry(r => r.Intervals(
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(30)
                ));

                // Servis-spesifik topoloji konfigürasyonu (exchange isimleri, tipleri vb.)
                configureTopology?.Invoke(cfg);

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}

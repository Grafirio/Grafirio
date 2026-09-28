using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Grafirio.Telemetry;

public static class TelemetryExtensions
{
    /// <summary>
    /// OTLP hedefinin adresi. Ortam degiskeni olarak da (ayni adla) okunuyor;
    /// OpenTelemetry'nin kendi standart adi bu.
    /// </summary>
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Iz, olcum ve logu tek yerde kurar.
    ///
    /// Disari aktarim yalnizca <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> tanimliysa
    /// acilir. Tanimsizken servis eskisi gibi calisir: olcumler uretilir ama
    /// hicbir yere gonderilmez, yani bu kurulum bir ortama gecerken ek bir
    /// bagimlilik (toplayici ayakta mi?) getirmiyor.
    /// </summary>
    public static IHostApplicationBuilder AddGrafirioTelemetry(
        this IHostApplicationBuilder builder, string serviceName)
    {
        var exportEnabled = !string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            // Log satiri kendi iz kimligini tasiyor; yavas bir istegin loglari
            // izinden tek tikla bulunabiliyor.
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: GrafirioTelemetry.Version)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = builder.Environment.EnvironmentName
                }))
            .WithTracing(tracing => tracing
                .AddSource(GrafirioTelemetry.SourceName)
                // Npgsql ve MassTransit kendi ActivitySource'larini yayinliyor;
                // yalnizca dinlemek yetiyor, paket gerekmiyor.
                .AddSource("Npgsql")
                .AddSource("MassTransit")
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Saglik ve sayac uclari her birkac saniyede bir yoklaniyor;
                    // izlerin cogunu onlar olusturup asil trafigi gomuyordu.
                    options.Filter = context => !IsNoise(context.Request.Path);
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation(options => options.RecordException = true))
            .WithMetrics(metrics => metrics
                .AddMeter(GrafirioTelemetry.SourceName)
                .AddMeter("Npgsql")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // CPU, bellek, GC, is parcacigi havuzu: "istek basina ne kadar
                // kaynak harcaniyor" sorusunun cevabi buradan cikiyor.
                .AddRuntimeInstrumentation()
                // Sure histogramlarinin varsayilan kovalari (0..10 sn) LLM ve
                // analiz isleri icin cok dar: dakikalar suren bir cagri tek bir
                // "10+" kovasina dusup p95'i anlamsizlastiriyordu.
                .AddView(instrument => instrument.Unit == "s" && instrument.Meter.Name == GrafirioTelemetry.SourceName
                    ? new ExplicitBucketHistogramConfiguration { Boundaries = DurationBuckets }
                    : null));

        if (exportEnabled) otel.UseOtlpExporter();

        return builder;
    }

    /// <summary>
    /// Milisaniyelik SQL'den dakikalik analize kadar ayni histogram kullaniliyor;
    /// kovalar bu araligin tamamini kapsiyor.
    /// </summary>
    private static readonly double[] DurationBuckets =
    [
        0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20, 30, 60, 120, 300, 600, 1200
    ];

    private static bool IsNoise(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics");
}

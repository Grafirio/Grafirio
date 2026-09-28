using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Grafirio.Telemetry;

/// <summary>
/// Butun servislerin ortak olcum kaynagi.
///
/// Tek bir ad kullaniliyor ("Grafirio"): izleme tarafinda servisler zaten
/// <c>service.name</c> ile ayriliyor, kaynak adini servise gore bolmek her
/// yeni servis icin kurulumda bir satir daha unutulacak yer acmak olurdu.
///
/// Olcum adlari OpenTelemetry anlam kurallarina gore: noktali, kucuk harf,
/// birim adin icinde degil <c>unit</c> alaninda.
/// </summary>
public static class GrafirioTelemetry
{
    public const string SourceName = "Grafirio";

    public static readonly string Version =
        typeof(GrafirioTelemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    public static readonly Meter Meter = new(SourceName, Version);

    /// <summary>
    /// Bir islemin suresini saniye cinsinden kaydeder. Saniye, OpenTelemetry'nin
    /// sure histogramlari icin onerdigi birim; ASP.NET Core'un kendi
    /// <c>http.server.request.duration</c> olcumu de saniye — ayni panoda yan yana
    /// durabilmeleri icin ayni birimde olmalilar.
    /// </summary>
    public static double Seconds(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
}

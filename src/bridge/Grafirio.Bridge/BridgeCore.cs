using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Grafirio.Bridge;

/// <summary>
/// Bridge'in cekirdegi: iki kabugun da kaydettigi servisler.
///
/// Ayni cekirdek iki yerde calisiyor — konsol/servis surumu
/// (<c>Grafirio.Bridge</c>) ve pencereli surum
/// (<c>Grafirio.Bridge.Desktop</c>). Kayitlar iki <c>Program</c> dosyasina
/// kopyalansaydi, biri digerinden sessizce ayrilirdi: eklenen bir servis tek
/// kabukta calisir, oburunde "No service for type" ile duserdi.
///
/// Burada olmayan tek sey <see cref="IBridgeDisplay"/>: kurulumun kullaniciya
/// nasil gosterilecegi kabugun kendi karari ve tek fark orasi.
/// </summary>
public static class BridgeCore
{
    /// <param name="runInBackground">
    /// Servis surumunde <c>true</c>: acilir acilmaz kaydolmayi ve baglanmayi
    /// dener. Masaustu kabugunda <c>false</c> — orada kurulum, kullanicinin
    /// "Giriş Yap" dugmesine basmasiyla basliyor ve isci ondan sonra elle
    /// baslatiliyor. Otomatik baslasaydi, uygulama acilir acilmaz kimsenin
    /// istemedigi bir kurulum akisi yuruturdu.
    /// </param>
    public static IServiceCollection AddBridgeCore(
        this IServiceCollection services, bool runInBackground = true)
    {
        // Yollar yapilandirmadan geliyor ama varsayilanlari ProgramData
        // altinda: calisma dizini servis hesabina gore degisiyor ve gorece yol
        // yazmak, durum dosyasinin nereye dustugunu tahmin edilemez yapiyor.
        services.AddSingleton(provider => new BridgeState(
            provider.GetRequiredService<ILogger<BridgeState>>(),
            Resolve(Options(provider).StatePath, "state.dat")));

        services.AddSingleton(provider => new QueryAuditLog(
            provider.GetRequiredService<ILogger<QueryAuditLog>>(),
            Resolve(Options(provider).AuditLogPath, "audit.tsv")));

        services.AddSingleton<BridgeDeviceLogin>();
        services.AddSingleton<BridgeEnrollment>();
        services.AddSingleton<QueryExecutor>();
        services.AddSingleton<BridgeQueryPump>();
        services.AddSingleton<BridgeTokenSource>();

        services.AddSingleton<BridgeWorker>();
        if (runInBackground)
            services.AddHostedService(provider => provider.GetRequiredService<BridgeWorker>());

        return services;
    }

    private static BridgeOptions Options(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<BridgeOptions>>().Value;

    /// <summary>Gorece yollari ProgramData altina baglar.</summary>
    public static string Resolve(string configured, string fallbackFileName)
    {
        if (Path.IsPathRooted(configured)) return configured;

        return Path.Combine(DataDirectory,
            string.IsNullOrWhiteSpace(configured) ? fallbackFileName : configured);
    }

    /// <summary>Durum ve denetim dosyalarinin bulundugu klasor.</summary>
    public static string DataDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Grafirio", "Bridge")
        : "/var/lib/grafirio-bridge";
}

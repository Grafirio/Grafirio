using Grafirio.Bridge;

var builder = Host.CreateApplicationBuilder(args);

// Windows servisi olarak kurulabilsin. Konsoldan calistirildiginda bu satir
// bir sey degistirmiyor, yani hata ayiklama icin de ayni ikili kullanilabilir.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Grafirio Bridge";
});

builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));

// Yollar yapilandirmadan geliyor ama varsayilanlari ProgramData altinda:
// calisma dizini servis hesabina gore degisiyor ve gorece yol yazmak,
// durum dosyasinin nereye dustugunu tahmin edilemez yapiyor.
builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<BridgeOptions>>().Value;

    return new BridgeState(
        provider.GetRequiredService<ILogger<BridgeState>>(),
        Resolve(options.StatePath, "state.dat"));
});

builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<BridgeOptions>>().Value;

    return new QueryAuditLog(
        provider.GetRequiredService<ILogger<QueryAuditLog>>(),
        Resolve(options.AuditLogPath, "audit.tsv"));
});

builder.Services.AddSingleton<BridgeEnrollment>();
builder.Services.AddSingleton<QueryExecutor>();
builder.Services.AddSingleton<BridgeQueryPump>();
builder.Services.AddSingleton<BridgeTokenSource>();
builder.Services.AddHostedService<BridgeWorker>();

var host = builder.Build();
host.Run();

static string Resolve(string configured, string fallbackFileName)
{
    if (Path.IsPathRooted(configured)) return configured;

    var baseDirectory = OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Grafirio", "Bridge")
        : "/var/lib/grafirio-bridge";

    return Path.Combine(baseDirectory,
        string.IsNullOrWhiteSpace(configured) ? fallbackFileName : configured);
}

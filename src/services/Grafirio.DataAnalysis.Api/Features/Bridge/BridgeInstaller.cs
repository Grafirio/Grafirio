using Microsoft.Extensions.FileProviders;

namespace Grafirio.DataAnalysis.Api.Features.Bridge;

/// <summary>
/// Kurulum dosyasinin nereden verilecegi.
///
/// Bu ucun var olma sebebi somut: panel "kurulum dosyasini hedef sunucuya
/// kopyalayin" diyordu ve dosyanin nereden alinacagi hicbir yerde yazmiyordu.
/// <c>/enroll</c> de surum uyusmazliginda "guncel kurulum dosyasini indirin"
/// diyor — isaret ettigi adres burasi.
///
/// Iki calisma bicimi var:
///   * <c>BridgeInstaller:Url</c> verilmisse oraya yonlendiriliyor (blob/CDN).
///     Uretimde beklenen bu: 170 MB'lik bir dosyayi API uzerinden akitmak,
///     servisin isi degil.
///   * <c>BridgeInstaller:Path</c> verilmisse dosya dogrudan sunuluyor. Yerel
///     gelistirme ve tek makineye kurulan surumler icin.
///
/// Ikisi de yoksa uc 404 donuyor ve SEBEBINI soyluyor: "dosya yok" ile
/// "yapilandirilmamis" arasindaki fark, bakan kisinin nereye bakacagini
/// belirliyor.
/// </summary>
public class BridgeInstaller(IConfiguration configuration, ILogger<BridgeInstaller> logger)
{
    /// <summary>Kullaniciya inen dosyanin adi.</summary>
    public const string FileName = "GrafirioSetup.exe";

    private string? Url => Trimmed("BridgeInstaller:Url");

    private string? Path => Trimmed("BridgeInstaller:Path");

    private string? Trimmed(string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Panelin indirme dugmesini gosterip gostermeyecegine karar vermesi icin.
    /// Olmayan bir dosyaya dugme koymak, tiklayana kadar calisiyor gorunen bir
    /// arayuz demek olurdu.
    /// </summary>
    public InstallerInfo Describe()
    {
        if (Url is { } url)
            return new InstallerInfo(true, FileName, null, null, url);

        if (Path is { } path && File.Exists(path))
        {
            var file = new FileInfo(path);
            return new InstallerInfo(true, FileName, file.Length, file.LastWriteTimeUtc, null);
        }

        return new InstallerInfo(false, FileName, null, null, null);
    }

    /// <summary>
    /// Indirme cevabi. Yonlendirme ya da dosya; yoksa sebebi yazan bir 404.
    /// </summary>
    public IResult Serve()
    {
        if (Url is { } url) return Results.Redirect(url, permanent: false);

        if (Path is not { } path)
        {
            logger.LogWarning(
                "Kurulum dosyası yapılandırılmamış. BridgeInstaller__Url ya da " +
                "BridgeInstaller__Path verilmeli.");

            return Results.NotFound(new
            {
                error = "Kurulum dosyası bu ortamda yayınlanmamış. " +
                        "Sunucuda BridgeInstaller__Url ya da BridgeInstaller__Path tanımlanmalı."
            });
        }

        if (!File.Exists(path))
        {
            // Yol verilmis ama dosya yok: yapilandirma dogru, yayin adimi
            // eksik. Mesaj yolu iceriyor cunku bakilacak yer orasi.
            logger.LogError("Kurulum dosyası bulunamadı: {Path}", path);

            return Results.NotFound(new
            {
                error = $"Kurulum dosyası sunucuda bulunamadı ({path})."
            });
        }

        return Results.File(
            new PhysicalFileProvider(System.IO.Path.GetDirectoryName(path)!)
                .GetFileInfo(System.IO.Path.GetFileName(path))
                .CreateReadStream(),
            "application/octet-stream",
            FileName,
            enableRangeProcessing: true);
    }
}

/// <param name="Available">Indirilebilir mi.</param>
/// <param name="FileName">Kullaniciya inen dosya adi.</param>
/// <param name="SizeBytes">Yerel dosyada biliniyor; yonlendirmede bilinmiyor.</param>
/// <param name="PublishedAt">Dosyanin son degistirilme zamani.</param>
/// <param name="Url">Yonlendirilecek adres — panelde gosterilmiyor, tanı için.</param>
public record InstallerInfo(
    bool Available,
    string FileName,
    long? SizeBytes,
    DateTime? PublishedAt,
    string? Url);

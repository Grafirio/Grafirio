using SkiaSharp;

namespace Grafirio.Identity.Api.Features.Companies.Documents;

/// <summary>
/// Belgenin küçük bir önizlemesini üretir.
///
/// Şirket belgelerinin çoğu PDF olduğu için yalnızca resim desteklemek
/// önizlemeyi işe yaramaz hale getirirdi; PDF'in ilk sayfası da raster'lanıyor.
///
/// Üretim her koşulda hataya dayanıklı: PDF raster'lama native bir katmana
/// (PDFium) bağlı ve bu katman container görüntüsünde eksik olabilir. Böyle bir
/// durumda belgenin kendisi yine yüklenmeli — kullanıcı vergi levhasını
/// saklayamamasının sebebinin bir önizleme kütüphanesi olduğunu anlayamaz.
/// </summary>
public class CompanyDocumentThumbnailer(ILogger<CompanyDocumentThumbnailer> logger)
{
    /// Kart ızgarasında kullanılan boy. Ekranda ~96px görünüyor, iki katı
    /// yüksek çözünürlüklü ekranlarda bulanıklaşmasın diye.
    private const int MaxEdge = 192;

    public byte[]? TryCreate(byte[] content, string? contentType, string fileName)
    {
        try
        {
            using var bitmap = Decode(content, contentType, fileName);
            if (bitmap is null) return null;

            return Encode(bitmap);
        }
        catch (Exception ex)
        {
            // Yükleme akışını kesmiyoruz; önizleme boş kalır.
            logger.LogWarning(ex, "Belge önizlemesi üretilemedi: {FileName}", fileName);
            return null;
        }
    }

    private static SKBitmap? Decode(byte[] content, string? contentType, string fileName)
    {
        if (IsPdf(contentType, fileName))
        {
            // Yalnızca ilk sayfa: önizlemenin işi belgeyi tanıtmak, göstermek
            // değil. Index alan aşırı yükleme kullanılıyor, int alan olan
            // kullanımdan kalktı.
            return PDFtoImage.Conversion.ToImage(content, page: Index.Start);
        }

        // Tanınmayan biçimde SkiaSharp null dönmüyor, ArgumentNullException
        // fırlatıyor (kod çözücü kurulamadığında). Word/Excel gibi belgeler tam
        // olarak bu yola giriyor, yani istisna beklenen durum — çağıran taraftaki
        // try/catch onu yakalayıp önizlemesiz devam ediyor.
        return SKBitmap.Decode(content);
    }

    private static bool IsPdf(string? contentType, string fileName)
        => contentType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true
           || Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    private static byte[]? Encode(SKBitmap source)
    {
        if (source.Width == 0 || source.Height == 0) return null;

        // En boy oranı korunuyor; kırpmak yerine küçültüyoruz çünkü belgede
        // ayırt edici olan şey genelde üst kısmı (başlık, logo, damga).
        var scale = Math.Min((float)MaxEdge / source.Width, (float)MaxEdge / source.Height);
        if (scale > 1f) scale = 1f;

        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));

        using var resized = source.Resize(new SKImageInfo(width, height), SKFilterQuality.Medium);
        if (resized is null) return null;

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);

        return data?.ToArray();
    }
}

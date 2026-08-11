using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Grafirio.Bridge.Desktop;

/// <summary>
/// Marka isareti: pencere basligi, pencere ikonu ve tepsi simgesi.
///
/// Kaynak tek: <c>BrandMark.xaml</c> icindeki vektor. Tepsi simgesi de oradan
/// uretiliyor — hazir bir <c>.ico</c> tasimak, ayni seklin iki kopyasini
/// tutmak ve birinin gun gelip eski kalmasi demekti.
/// </summary>
public static class BrandIcon
{
    /// <summary>Pencerede ve baslikta kullanilan vektor.</summary>
    public static ImageSource Mark() =>
        (ImageSource)Application.Current.FindResource("BrandMark");

    /// <summary>
    /// Tepsi simgesi. Bagli degilken soluk: rozetin rengine bakmadan,
    /// simgeye goz atarak durumu anlamak icin.
    /// </summary>
    public static Icon Create(bool connected)
    {
        // 32 piksel: Windows tepside 16 kullaniyor ama yuksek DPI'da 32
        // isteniyor ve buyuk olani kucultmek, kucugu buyutmekten iyi.
        const int size = 32;

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            if (!connected) context.PushOpacity(0.45);

            context.DrawImage(Mark(), new Rect(0, 0, size, size));

            if (!connected) context.Pop();
        }

        var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);

        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        encoder.Save(stream);
        stream.Position = 0;

        using var bitmap = new Bitmap(stream);

        // GetHicon bir GDI tutamaci uretiyor ve Icon.FromHandle onu
        // sahiplenmiyor; kopyalayip aslini birakmak, uzun sure acik kalan bir
        // uygulamada tutamaclarin birikmesini onluyor.
        var handle = bitmap.GetHicon();

        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}

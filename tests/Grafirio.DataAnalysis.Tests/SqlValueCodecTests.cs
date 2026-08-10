using System.Globalization;
using Grafirio.Bridge.Contracts;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Degerlerin musteri agindan buluta gelirken bozulmamasi.
///
/// Burasi protokolun en sessiz kirilma noktasi: bozulan bir sayi hata vermez,
/// yalnizca yanlis bir grafik cizer. Iki senaryo ozellikle olculuyor:
///
///   * Kultur. Musteri sunucusu tr-TR olabilir; orada ondalik ayraci virgul.
///     "1,5" degeri buluta gidip 15 olarak okunursa kimse fark etmez.
///   * Tip. Her sey metne dususe sema profilindeki min/max karsilastirmasi
///     metin sirasina duser: sayisal kolonda 100 &lt; 99 cikar.
/// </summary>
public class SqlValueCodecTests
{
    /// <summary>
    /// Testler musteri makinesinin kulturunu taklit ediyor. Kodlayici
    /// InvariantCulture kullanmasaydi bu testler kirmizi donerdi — koruma
    /// tam olarak bu.
    /// </summary>
    private static T InTurkishCulture<T>(Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try { return action(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static object? RoundTrip(object? value)
    {
        var kind = SqlValueCodec.KindOf(value?.GetType());
        var encoded = SqlValueCodec.Encode(value);
        return SqlValueCodec.Decode(kind, encoded);
    }

    [Fact]
    public void Ondalik_sayi_turkce_kulturde_bozulmuyor()
    {
        var result = InTurkishCulture(() => RoundTrip(1.5m));

        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void Ondalik_sayi_metne_dusmuyor()
    {
        // Tur korunmazsa min/max karsilastirmasi metin sirasina duser.
        var result = RoundTrip(1234.56m);

        Assert.IsType<decimal>(result);
    }

    [Fact]
    public void Tam_sayilar_sayi_olarak_geri_geliyor()
    {
        Assert.Equal(100L, RoundTrip(100));
        Assert.Equal(-7L, RoundTrip((short)-7));
        Assert.IsType<long>(RoundTrip(42));
    }

    [Fact]
    public void Sayilar_metin_gibi_siralanmiyor()
    {
        // Metin sirasinda "100" < "99" olur. Asil korunan degismez bu.
        var hundred = (long)RoundTrip(100)!;
        var ninetyNine = (long)RoundTrip(99)!;

        Assert.True(hundred > ninetyNine);
    }

    [Fact]
    public void Tarih_gun_ay_karismadan_geri_geliyor()
    {
        var value = new DateTime(2026, 4, 3, 14, 30, 15);

        var result = InTurkishCulture(() => RoundTrip(value));

        Assert.Equal(value, result);
    }

    [Fact]
    public void Saat_dilimi_tasiyan_tarih_dilimini_koruyor()
    {
        var value = new DateTimeOffset(2026, 4, 3, 14, 30, 0, TimeSpan.FromHours(3));

        var result = RoundTrip(value);

        Assert.Equal(value, Assert.IsType<DateTimeOffset>(result));
    }

    [Fact]
    public void Bos_hucre_null_kaliyor()
    {
        Assert.Null(RoundTrip(null));
        Assert.Null(SqlValueCodec.Decode(SqlValueKind.Text, null));
    }

    [Fact]
    public void DBNull_de_null_sayiliyor()
    {
        Assert.Null(SqlValueCodec.Encode(DBNull.Value));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mantiksal_deger_korunuyor(bool value) =>
        Assert.Equal(value, RoundTrip(value));

    [Fact]
    public void Guid_korunuyor()
    {
        var value = Guid.NewGuid();
        Assert.Equal(value, RoundTrip(value));
    }

    [Fact]
    public void Ikili_veri_korunuyor()
    {
        var value = new byte[] { 1, 2, 250, 0 };
        Assert.Equal(value, RoundTrip(value));
    }

    [Fact]
    public void Bozuk_deger_sessizce_null_olmuyor()
    {
        // Sessizce null donmek, bos hucre ile bozuk hucreyi ayirt edilemez
        // yapar; analiz yanlis cikar ve sebebi hicbir yerde gorunmez.
        var ex = Assert.Throws<InvalidOperationException>(
            () => SqlValueCodec.Decode(SqlValueKind.Integer, "sayı değil"));

        Assert.Contains("çözülemedi", ex.Message);
    }

    [Fact]
    public void Metin_icindeki_sayi_metin_kaliyor()
    {
        // Kolon nvarchar ise "007" degeri 7 olmamali.
        var result = SqlValueCodec.Decode(SqlValueKind.Text, "007");

        Assert.Equal("007", result);
    }
}

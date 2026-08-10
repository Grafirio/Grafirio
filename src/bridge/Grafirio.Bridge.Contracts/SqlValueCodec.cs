using System.Globalization;

namespace Grafirio.Bridge.Contracts;

/// <summary>
/// Degerleri tel uzerinde tasinabilir hale getirir ve geri cevirir.
///
/// Iki taraf da BURAYI kullanmali. Kodlama ve cozme ayri yerlerde yazilirsa
/// aralarindaki fark yalnizca uretimde, yanlis bir sayida ortaya cikar.
///
/// Kultur her yerde <see cref="CultureInfo.InvariantCulture"/>: musteri
/// sunucusu tr-TR olabilir, orada ondalik ayraci virguldur. "1,5" degeri
/// buluta gidip 15 olarak okunursa kimse fark etmez.
///
/// Tarihler ISO 8601 ve saat dilimi bilgisiyle. Yerel bicimde "03.04.2026"
/// gun mu ay mi belli olmuyor.
/// </summary>
public static class SqlValueCodec
{
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffffff";
    private const string DateTimeOffsetFormat = "yyyy-MM-ddTHH:mm:ss.fffffffzzz";

    /// <summary>Bir CLR degerinin hangi tur olarak tasinacagi.</summary>
    public static SqlValueKind KindOf(Type? type) => type switch
    {
        null => SqlValueKind.Null,
        _ when type == typeof(string) || type == typeof(char) => SqlValueKind.Text,
        _ when type == typeof(byte) || type == typeof(short) || type == typeof(int)
            || type == typeof(long) || type == typeof(sbyte) || type == typeof(ushort)
            || type == typeof(uint) || type == typeof(ulong) => SqlValueKind.Integer,
        _ when type == typeof(decimal) || type == typeof(double)
            || type == typeof(float) => SqlValueKind.Decimal,
        _ when type == typeof(bool) => SqlValueKind.Boolean,
        _ when type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly) || type == typeof(TimeSpan) => SqlValueKind.DateTime,
        _ when type == typeof(Guid) => SqlValueKind.Guid,
        _ when type == typeof(byte[]) => SqlValueKind.Binary,
        // Taninmayan tip metne dusuyor. Veriyi kaybetmektense okunabilir
        // haliyle tasimak yeglenir; hangi tip oldugu loga yazilmiyor cunku
        // deger hassas olabilir.
        _ => SqlValueKind.Text,
    };

    public static string? Encode(object? value) => value switch
    {
        null or DBNull => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>
    /// Cozulemeyen deger sessizce <c>null</c>'a dusmez: bozuk bir sayi, bos bir
    /// hucreden farklidir ve fark edilmeden gecerse analiz yanlis cikar.
    /// </summary>
    public static object? Decode(SqlValueKind kind, string? value)
    {
        if (value is null || kind == SqlValueKind.Null) return null;

        try
        {
            return kind switch
            {
                SqlValueKind.Text => value,
                SqlValueKind.Integer => long.Parse(value, CultureInfo.InvariantCulture),
                SqlValueKind.Decimal => decimal.Parse(
                    value, NumberStyles.Float, CultureInfo.InvariantCulture),
                SqlValueKind.Boolean => value is "true" or "True" or "1",
                SqlValueKind.DateTime => DecodeTemporal(value),
                SqlValueKind.Guid => Guid.Parse(value),
                SqlValueKind.Binary => Convert.FromBase64String(value),
                _ => value,
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new InvalidOperationException(
                $"'{kind}' türündeki bir değer çözülemedi. Bridge ile sunucu " +
                "sürümleri uyumsuz olabilir.", ex);
        }
    }

    private static object DecodeTemporal(string value)
    {
        // Saat dilimi tasiyan degerler DateTimeOffset olarak geri veriliyor;
        // tasimayanlar DateTime. Ikisini tek tipe indirmek, ya saat dilimini
        // uydurmak ya da atmak olurdu.
        if (DateTimeOffset.TryParseExact(value, DateTimeOffsetFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset))
            return offset;

        if (DateTime.TryParseExact(value, DateTimeFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
            return dateTime;

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        return TimeSpan.ParseExact(value, "c", CultureInfo.InvariantCulture);
    }
}

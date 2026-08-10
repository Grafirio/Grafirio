using System.Text.RegularExpressions;

namespace Grafirio.Bridge;

/// <summary>
/// "Bu sorgu yalnizca okuyor mu?"
///
/// Sunucu tarafinda ayni kural var (<c>ReadOnlySqlPolicy</c>) ve orada da
/// uygulaniyor. Ikisi kasitli olarak ayri: sunucudaki kontrol hatayi erken
/// gostermek icin, buradaki ise musteriye verilen sozu tutmak icin. Bridge,
/// buluta guvenmemeli — kod paylasilsaydi, bulut tarafi degistirildiginde
/// musterinin korumasi da degismis olurdu.
/// </summary>
public static class ReadOnlySql
{
    public static bool IsReadOnly(string sql)
    {
        var stripped = StripLeadingNoise(sql);

        var startsRead =
            stripped.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            stripped.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

        if (!startsRead) return false;

        // Sondaki noktali virgul zararsiz; arada olan, ikinci bir ifade demek.
        var trimmed = stripped.TrimEnd().TrimEnd(';');
        if (trimmed.Contains(';')) return false;

        // Okuma gibi baslayip yazmaya donen bicimler. `SELECT ... INTO yeni_tablo`
        // tablo olusturur; `FOR UPDATE` ve `EXEC` de okuma degildir.
        return !ForbiddenInsideRead.IsMatch(trimmed);
    }

    /// <summary>
    /// <b>CultureInvariant sart.</b> Turkce kulturde 'i' harfinin buyugu 'İ',
    /// 'I' harfinin kucugu 'ı'; yani <c>IgnoreCase</c> tek basina "into" ile
    /// "INTO"yu ESLESTIRMIYOR. Musteri sunucularinin cogu tr-TR ve bu, korumanin
    /// tam da onlarda calismamasi demekti. Ayni sebeple <see cref="SqlTableScanner"/>
    /// tarafinda "join" de yakalanmiyordu.
    /// </summary>
    private static readonly Regex ForbiddenInsideRead = new(
        @"\b(INTO\s+(?!\s*\()|EXEC(UTE)?\s|MERGE\s|INSERT\s|UPDATE\s|DELETE\s|DROP\s|ALTER\s|CREATE\s|TRUNCATE\s|GRANT\s|REVOKE\s)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string StripLeadingNoise(string sql)
    {
        var text = sql.TrimStart();

        while (true)
        {
            if (text.StartsWith("--", StringComparison.Ordinal))
            {
                var newline = text.IndexOf('\n');
                if (newline < 0) return "";
                text = text[(newline + 1)..].TrimStart();
                continue;
            }

            if (text.StartsWith("/*", StringComparison.Ordinal))
            {
                var close = text.IndexOf("*/", StringComparison.Ordinal);
                if (close < 0) return "";
                text = text[(close + 2)..].TrimStart();
                continue;
            }

            return text;
        }
    }
}

/// <summary>
/// Sorgu metninde gecen tablo adlarini bulur.
///
/// Bu bir SQL ayristiricisi degil ve oyle olmaya calismiyor. Amac, izin
/// listesi yapilandirilmis bir kurulumda listede olmayan bir tabloya
/// gidilmesini yakalamak. Fazla yakalamak (yanlis pozitif) kabul edilebilir:
/// sonucu bir sorgunun reddedilmesi. Az yakalamak kabul edilemez.
/// </summary>
public static class SqlTableScanner
{
    // CultureInvariant sart: tr-TR'de "join" ile "JOIN" eslesmez ve izin
    // listesi disi bir tabloya giden JOIN gorunmez olurdu.
    private static readonly Regex TableReference = new(
        @"\b(?:FROM|JOIN|UPDATE|INTO)\s+((?:\[[^\]]+\]|[A-Za-z_][\w$#]*)(?:\s*\.\s*(?:\[[^\]]+\]|[A-Za-z_][\w$#]*))*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IEnumerable<string> ReferencedTables(string sql)
    {
        foreach (Match match in TableReference.Matches(sql))
        {
            var name = match.Groups[1].Value.Replace(" ", "");

            // Alt sorgu ya da tablo degeri donduren fonksiyon degil, gercek ad.
            if (name.Length > 0) yield return name;
        }
    }
}

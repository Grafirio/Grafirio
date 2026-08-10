namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// "Bu sorgu yalnizca okuyor mu?"
///
/// Uretilen SQL bir LLM'in etkiledigi metinden cikiyor ve musteri
/// veritabaninda kosuyor. Sistem hicbir yerde yazma yetkisi gerektiren bir is
/// yapmiyor; dolayisiyla okuma disi her sorgu, ya bir hata ya da bir saldiri.
///
/// Kural burada duruyor cunku iki ayri yerde uygulanacak: bugun PyCaret'in
/// veri okudugu ic ucta, Faz 2'de de musteri agindaki bridge'te. Bridge'in bunu
/// kendi basina yeniden uygulamasi sart — musteriye verilen soz "buluttan
/// gelen sorgu veritabaninizi degistiremez" ise, o kontrolun musterinin
/// tarafinda olmasi gerekir.
/// </summary>
public static class ReadOnlySqlPolicy
{
    /// <summary>
    /// Sorgunun okuma oldugunu dogrular. Yorum satirlari ve bosluklar atlanip
    /// ilk anahtar kelimeye bakilir; ayrica noktali virgulle ikinci bir ifade
    /// eklenmesi engellenir.
    /// </summary>
    public static bool IsReadOnly(string sql)
    {
        var stripped = StripLeadingNoise(sql);

        var startsRead =
            stripped.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            stripped.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

        if (!startsRead) return false;

        // Sondaki noktali virgul zararsiz; arada olan, ikinci bir ifade demek.
        var trimmed = stripped.TrimEnd().TrimEnd(';');
        return !trimmed.Contains(';');
    }

    /// <summary>
    /// Bastaki bosluk ve yorumlari atar. Yorum kapanmiyorsa sorgunun geri
    /// kalani okunamiyor demektir; bu durumda bos donuyor ve sorgu reddediliyor.
    /// </summary>
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

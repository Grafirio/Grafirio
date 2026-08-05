using System.Text.RegularExpressions;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>
/// Hangi kolondan ornek deger alinabilecegine karar verir.
///
/// Ornek degerler LLM'e gonderiliyor; yani musteri veritabanindaki icerik dis
/// bir servise cikiyor. Kural seti musterinin koydugu cerceveden geliyor:
/// TC, telefon ve adres hicbir kosulda gonderilmez; konumda en fazla sehir.
///
/// Uzunluk esigine dayanmiyoruz: bir firma adi 150 karakter olabilir ve onu
/// kacirmak, esleme kalitesini dusururdu. Ayrim iki sey uzerinden yapiliyor:
/// kolonun adi/icerigi hassas mi, ve kardinalitesi ne.
/// </summary>
public static class SensitiveColumnPolicy
{
    /// <summary>Hicbir kosulda ornek alinmayan kolonlar.</summary>
    private static readonly (string Label, Regex Pattern)[] DeniedByName =
    [
        ("kimlik no", new Regex(@"tckn|tc_?kimlik|kimlik_?no|national_?id|ssn|vergi_?no|tax_?no|passport", RegexOptions.IgnoreCase)),
        ("telefon", new Regex(@"phone|telefon|gsm|mobile|cep|fax|faks", RegexOptions.IgnoreCase)),
        ("e-posta", new Regex(@"email|e_?mail|eposta|e_?posta", RegexOptions.IgnoreCase)),
        ("adres", new Regex(@"address|adres|street|sokak|cadde|mahalle|apartman|bina|posta_?kodu|zip|postal", RegexOptions.IgnoreCase)),
        ("finansal kimlik", new Regex(@"iban|card_?no|kart_?no|credit_?card|cvv|account_?no|hesap_?no", RegexOptions.IgnoreCase)),
        ("kimlik bilgisi", new Regex(@"password|parola|sifre|token|secret|api_?key", RegexOptions.IgnoreCase)),
        ("kisi adi", new Regex(@"first_?name|last_?name|ad_?soyad|adsoyad|fullname|full_?name|surname|soyad", RegexOptions.IgnoreCase)),
        ("dogum", new Regex(@"birth|dogum|dogum_?tarih", RegexOptions.IgnoreCase)),
    ];

    /// <summary>
    /// Konum kolonlarinda izin verilen en ince kirilim. Sehir serbest;
    /// ulke ve bolge de zaten sehirden daha kaba oldugu icin serbest.
    /// </summary>
    private static readonly Regex AllowedLocation =
        new(@"city|sehir|şehir|il_?adi|ilce|ilçe|country|ulke|ülke|region|bolge|bölge", RegexOptions.IgnoreCase);

    /// <summary>Icerigi hassas desene uyan degerler (kolon adi masum olsa bile).</summary>
    private static readonly (string Label, Regex Pattern)[] DeniedByValue =
    [
        ("e-posta", new Regex(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$")),
        ("TC kimlik", new Regex(@"^[1-9][0-9]{10}$")),
        ("telefon", new Regex(@"^(\+?\d[\d\s\-\(\)]{9,17})$")),
        ("IBAN", new Regex(@"^[A-Z]{2}\d{2}[A-Z0-9]{10,30}$", RegexOptions.IgnoreCase)),
    ];

    public enum Decision
    {
        /// <summary>Ornek deger alinabilir.</summary>
        Allowed,

        /// <summary>Hassas: yalnizca sekil bilgisi (uzunluk, tip) gonderilir.</summary>
        Denied,

        /// <summary>Yuksek kardinaliteli serbest metin: yalnizca onay varsa.</summary>
        NeedsConsent
    }

    public sealed record Result(Decision Decision, string? Reason);

    /// <summary>
    /// Kolon adi ve kardinaliteye gore karar verir.
    /// </summary>
    /// <param name="distinctCount">null ise bilinmiyor demektir; temkinli davranilir.</param>
    public static Result Evaluate(string columnName, string dataType, int? distinctCount, int lowCardinalityThreshold = 50)
    {
        // Konum kolonlari once bakilir: "city" icinde "adres" gecmez ama
        // "delivery_address_city" gibi adlar her iki desene de uyabilir.
        // Sehir/ulke kirilimi musteri tarafindan acikca serbest birakildi.
        var isAllowedLocation = AllowedLocation.IsMatch(columnName);

        foreach (var (label, pattern) in DeniedByName)
        {
            if (!pattern.IsMatch(columnName)) continue;
            if (isAllowedLocation && label == "adres") continue; // sehir/ilce serbest
            return new Result(Decision.Denied, $"{label} içerebilir");
        }

        // Sayisal ve tarih kolonlarinda kisisel veri riski dusuk.
        if (IsNumericOrDate(dataType))
            return new Result(Decision.Allowed, null);

        // Dusuk kardinalite = kategorik alan (ulke, durum, tip). Bunlar
        // eslemenin en degerli sinyali ve kisisel veri tasima ihtimali dusuk.
        if (distinctCount is not null && distinctCount <= lowCardinalityThreshold)
            return new Result(Decision.Allowed, null);

        // Geri kalan serbest metin: firma adi burada, ve onu kacirmak
        // istemiyoruz — ama onaysiz da gondermiyoruz.
        return new Result(Decision.NeedsConsent, "yüksek kardinaliteli serbest metin");
    }

    /// <summary>
    /// Deger duzeyinde son suzgec: kolon adi masum olsa bile icerik hassas
    /// desene uyuyorsa o deger disari cikmaz.
    /// </summary>
    public static bool IsValueSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var trimmed = value.Trim();
        return !DeniedByValue.Any(d => d.Pattern.IsMatch(trimmed));
    }

    private static bool IsNumericOrDate(string dataType) =>
        dataType.ToLowerInvariant() is
            "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric" or
            "float" or "real" or "money" or "smallmoney" or "bit" or
            "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "time";
}

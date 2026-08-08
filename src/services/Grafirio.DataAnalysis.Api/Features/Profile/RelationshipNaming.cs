using System.Text;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>
/// Iliski cikariminin saf ad mantigi: veritabanina dokunmaz, durum tutmaz.
///
/// <see cref="RelationshipDiscovery"/>'den ayri duruyor cunku hatanin
/// saklanacagi yer burasi. "MusteriKodu" ile "Musteriler.Kod" arasindaki
/// baglantiyi kuran ya da kuramayan kod bu; yanlis calistiginda sistem
/// sessizce yanlis tabloya baglanir. Ayri ve saf oldugu icin veritabani
/// olmadan test edilebiliyor.
/// </summary>
public static class RelationshipNaming
{
    /// <summary>
    /// Bir kolonun baska bir tabloya isaret ettigini dusundurten ekler.
    /// Uzundan kisaya siralanmali: "refid", "id"den once denenmeli.
    /// </summary>
    private static readonly string[] ReferenceSuffixes =
        ["numarasi", "refid", "kodu", "guid", "code", "kod", "ref", "no", "id"];

    /// <summary>
    /// Kolon adindan referans ekini atar. Ek yoksa <c>null</c> — yani bu kolon
    /// bir baska tabloya isaret ediyor gorunmuyor.
    ///
    /// "ReceiverCompanyId" -> "ReceiverCompany"
    /// "MusteriKodu"       -> "Musteri"
    /// "Aciklama"          -> null
    /// </summary>
    public static string? StripReferenceSuffix(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return null;

        foreach (var suffix in ReferenceSuffixes)
        {
            if (columnName.Length <= suffix.Length) continue;
            if (!columnName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

            var stem = columnName[..^suffix.Length].Trim('_', ' ');
            if (stem.Length >= 2) return stem;
        }

        return null;
    }

    /// <summary>
    /// PascalCase kelime sinirlarindan kuyruk parcalari, uzundan kisaya.
    ///
    /// "ReceiverCompany" -> ["ReceiverCompany", "Company"]
    ///
    /// Rol oneki tasiyan kolonlarin cozumu bu: <c>SenderCompanyId</c> ve
    /// <c>ReceiverCompanyId</c> ayni <c>Companies</c> tablosuna gider ama
    /// anlamlari farklidir; onek korunarak denenip sonra atiliyor.
    /// </summary>
    public static IEnumerable<string> TailSegments(string value)
    {
        if (string.IsNullOrEmpty(value)) yield break;

        yield return value;

        for (var i = 1; i < value.Length; i++)
            if (char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
                yield return value[i..];
    }

    /// <summary>
    /// Bir adin anlamli kuyruk parcalari, UZUNDAN KISAYA.
    ///
    /// "L_INT_ExportReference" -> ["L_INT_ExportReference", "INT_ExportReference",
    ///                             "ExportReference", "Reference"]
    ///
    /// Gercek musteri semalarinda tablo adlari sistem oneki tasiyor:
    /// <c>L_INT_ExportReference</c>, <c>L_ROD_ExportPosition</c>. Yalnizca tam
    /// adi karsilastirmak, <c>ReferenceId</c> kolonunun <c>...ExportReference</c>
    /// tablosuna isaret ettigini goremiyordu — normalize edilmis hali
    /// "lintexportreference" iken kolonun koku "reference" kaliyordu.
    ///
    /// Alt cizgi VE PascalCase sinirlarindan bolunuyor; en uzun parca once
    /// denensin diye sirali.
    /// </summary>
    public static IEnumerable<string> NameSegments(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;

        var tokens = new List<string>();
        foreach (var part in value.Split(['_', '-', ' ', '.'], StringSplitOptions.RemoveEmptyEntries))
        {
            var start = 0;
            for (var i = 1; i < part.Length; i++)
            {
                if (!char.IsUpper(part[i]) || char.IsUpper(part[i - 1])) continue;
                tokens.Add(part[start..i]);
                start = i;
            }
            tokens.Add(part[start..]);
        }

        for (var i = 0; i < tokens.Count; i++)
            yield return string.Concat(tokens.Skip(i));
    }

    /// <summary>
    /// Kucuk harfe indirir ve harf/rakam disi karakterleri atar. Cogul eki
    /// DOKUNULMAZ — bkz. <see cref="Variants"/>.
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);

        return builder.ToString();
    }

    /// <summary>
    /// Bir adin esdeger yazilislari: kendisi ve cogul eki dusurulmus hali.
    /// Iki ad, herhangi bir varyantlari ortusuyorsa ayni seyi anlatiyor sayilir.
    ///
    /// Neden varyant uretiliyor da tek bicime indirgenmiyor: cogul ekini
    /// yikici bicimde kirpmak Turkce'de bozuyor. "Adres" -> "adr",
    /// "Ders" -> "der", "Siparis" -> "sipari" oluyordu; sonuncusu yuzunden
    /// <c>SiparisNo</c> kolonu <c>Siparisler</c> tablosunu hic bulamiyordu
    /// (tablo "siparis"e inerken kolon "sipari"ye iniyordu). Orijinali her
    /// zaman listede tutmak bu sinifi tamamen ortadan kaldiriyor.
    ///
    /// Fazladan uretilen varyantin yanlis eslesme riski var; onu deger
    /// ortusmesi eliyor. Bulunamayan baglantiyi ise hicbir sey kurtarmaz —
    /// o yuzden burada comert, dogrulamada sert davraniliyor.
    /// </summary>
    public static IReadOnlyCollection<string> Variants(string value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0) return [];

        var variants = new HashSet<string>(StringComparer.Ordinal) { normalized };

        // "companies" -> "company"
        if (normalized.Length > 4 && normalized.EndsWith("ies", StringComparison.Ordinal))
            variants.Add(normalized[..^3] + "y");

        foreach (var plural in new[] { "leri", "lari", "ler", "lar", "es", "s" })
        {
            if (normalized.Length > plural.Length + 1
                && normalized.EndsWith(plural, StringComparison.Ordinal))
            {
                variants.Add(normalized[..^plural.Length]);
            }
        }

        return variants;
    }

    /// <summary>Iki ad ayni varligi mi anlatiyor.</summary>
    public static bool NamesMatch(string left, string right) =>
        Variants(left).Intersect(Variants(right), StringComparer.Ordinal).Any();

    /// <summary>
    /// Tip uyumu. Kesin esitlik aranmiyor: <c>int</c> ile <c>bigint</c>,
    /// <c>varchar</c> ile <c>nvarchar</c> pratikte eslesir. Amac imkansiz
    /// adaylari elemek — tarih kolonunu metin anahtara baglamak gibi.
    /// </summary>
    public static bool TypesCompatible(string left, string right) =>
        Family(left) == Family(right);

    private static string Family(string dataType) => (dataType ?? "").ToLowerInvariant() switch
    {
        "tinyint" or "smallint" or "int" or "bigint" or "numeric" or "decimal" => "number",
        "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" => "text",
        "uniqueidentifier" => "guid",
        var other => other,
    };

    /// <summary>Hedef tabloda "bu kaydin adi" olabilecek kolon adi kaliplari.</summary>
    public static readonly string[] LabelHints =
        ["unvan", "name", "adi", "ad", "title", "baslik", "tanim", "aciklama", "description", "label"];

    public static bool IsTextual(string dataType) => Family(dataType) == "text";
}

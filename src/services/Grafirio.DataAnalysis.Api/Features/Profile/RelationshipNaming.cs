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
    /// "L_INT_ExportReference" -> ["LINTExportReference", "INTExportReference",
    ///                             "ExportReference", "Reference"]
    ///
    /// Ayiricilar dusuyor; sorun degil, karsilastirma zaten
    /// <see cref="Normalize"/> uzerinden yapiliyor ve o da harf disi her seyi
    /// atiyor.
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

    /// <summary>
    /// Iki ad ayni varligi mi anlatiyor — yazim farki KABUL EDILMEDEN.
    ///
    /// <see cref="NamesMatch"/> ayrimi icin var: iki tablo parcasi da ayni
    /// koke uyduğunda tam yazilani kazanmali. Yoksa daha uzun ama yanlis
    /// yazilmis bir parca, tam eslesen kisa parcayi otelerdi.
    /// </summary>
    public static bool NamesMatchExactly(string left, string right) =>
        Variants(left).Intersect(Variants(right), StringComparer.Ordinal).Any();

    /// <summary>
    /// Iki ad ayni varligi mi anlatiyor. Tam eslesme yoksa TEK harflik yazim
    /// farki da kabul edilir — bkz. <see cref="IsSingleTypoApart"/>.
    /// </summary>
    public static bool NamesMatch(string left, string right)
    {
        var leftVariants = Variants(left);
        var rightVariants = Variants(right);

        if (leftVariants.Intersect(rightVariants, StringComparer.Ordinal).Any())
            return true;

        return leftVariants.Any(l => rightVariants.Any(r => IsSingleTypoApart(l, r)));
    }

    /// <summary>
    /// Bu uzunlugun altinda yazim farki hic kabul edilmiyor.
    ///
    /// Kisa adlarda tek harf her seyi degistirir: bes harfli iki ad arasindaki
    /// tek fark adin besde biridir, ve semalarda kisa token bollugu var.
    /// Aranan vakalar ("Referance"/"Reference") zaten uzun.
    /// </summary>
    private const int MinTypoLength = 6;

    /// <summary>
    /// Iki normalize ad, ESIT UZUNLUKTA tek harf ikamesiyle mi ayriliyor.
    ///
    /// Gercek vaka: ayni anlamdaki kolon iki tabloda iki farkli yazilmis —
    /// <c>L_INT_ExportReference.Refer<b>e</b>nceId</c> ile
    /// <c>...FinancialCalculate.Refer<b>a</b>nceId</c>. Tam esitlik arandigi
    /// icin kenar HIC uretilmiyordu.
    ///
    /// Uc koruma var, ucu de gercek tuzaklardan:
    ///
    /// <list type="bullet">
    /// <item>
    /// <b>Yalnizca ikame; ekleme/silme yok.</b> Plan "mesafe 1" diyordu, ki
    /// Levenshtein'da ekleme de buna girer — ama <c>Contact</c> /
    /// <c>Contract</c> tek harf eklemeyle ayriliyor ve ikisi de gercek
    /// semalarda yan yana duran, alakasiz tablolar. Esit uzunluk sarti bu
    /// sinifi tumden kapatiyor, aradigimiz vakayi disarida birakmadan.
    /// </item>
    /// <item>
    /// <b>Farkin rakam oldugu eslesmeler reddedilir.</b> <c>Adres1</c> /
    /// <c>Adres2</c> ayni sey degil, ayni seyin iki ornegi.
    /// </item>
    /// <item>
    /// <b>Asgari uzunluk.</b> Bkz. <see cref="MinTypoLength"/>.
    /// </item>
    /// </list>
    ///
    /// Mesafe 2'ye cikmak yok: <c>Order</c>/<c>Offer</c> ve
    /// <c>Import</c>/<c>Export</c> orada, ve bunlari karistirmak sozlugun
    /// "en pahali hata"si. Mesafe 1'de her ikisi de disarida kaliyor.
    ///
    /// Bu yalnizca ADAY uretir; deger ortusmesi kapisi arkada duruyor.
    /// </summary>
    public static bool IsSingleTypoApart(string left, string right)
    {
        if (left.Length != right.Length) return false;
        if (left.Length < MinTypoLength) return false;

        var difference = -1;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] == right[i]) continue;
            if (difference >= 0) return false;
            difference = i;
        }

        if (difference < 0) return false;

        return !char.IsDigit(left[difference]) && !char.IsDigit(right[difference]);
    }

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

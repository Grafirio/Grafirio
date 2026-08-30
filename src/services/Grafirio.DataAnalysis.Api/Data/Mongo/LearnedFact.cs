namespace Grafirio.DataAnalysis.Api.Data.Mongo;

/// <summary>
/// Veritabanini bilen bir insanin sisteme ogrettigi tek bir sey.
///
/// Bunlarin var olma sebebi: sozluk <c>AnalysisConfig.ConfigJson</c> icinde
/// yasiyor ve "Analiz Et" her calistiginda YENI bir config satiri uretilip
/// eskisi pasiflestiriliyor. Yani bugune kadar ogrenilen hicbir sey bir
/// sonraki analizi gecemiyordu — kullanici ogrettigini saniyor, sistem
/// unutuyordu. En kotu hata sinifi bu, cunku kimse fark etmiyor.
///
/// Kayitlar baglanti kimligine bagli, sozluge degil. Sozluk her analizde
/// yeniden uretiliyor; bunlar duruyor ve her uretimde ustune isleniyor.
/// </summary>
public sealed class LearnedFact
{
    /* ── Turler ───────────────────────────────────────────────────────────
       Kapali bir liste: bunlarin disinda hicbir sey ogrenilmiyor.

       Sebebi, bir cevabin SEMAYLA mi yoksa O ANKI SORUYLA mi ilgili
       oldugunu sistemin tek basina bilememesi. "Son 12 ay" ile "gelir
       dedigim EarningAmount" ayni cumlede gelebiliyor ama biri o sorunun
       tercihi, digeri veritabani hakkinda kalici bir gercek. Ilkini kalici
       ogrenmek, bundan sonraki her sorguyu kullanicinin goremeyecegi bir
       yerden sessizce filtrelemek olurdu. */

    /// <summary>İki tablonun hangi kolonlardan bağlandığı.</summary>
    public const string Relationship = "relationship";

    /// <summary>Kullanıcının bir kolona/tabloya verdiği ad ("gelir" → EarningAmount).</summary>
    public const string Synonym = "synonym";

    /// <summary>
    /// Bir tablonun ya da kolonun ne tuttugu. "Analiz Et" sorularinin
    /// cevaplari bu turden: <c>L_INT_ImportReference</c> = "ithalat kayitlari".
    /// Es anlamlidan farki, kullanicinin kelimesi degil TANIM olmasi.
    /// </summary>
    public const string Meaning = "meaning";

    /// <summary>Bir kod değerinin anlamı ("ROD" → karayolu).</summary>
    public const string CodeMeaning = "codeMeaning";

    /// <summary>Hedef tabloda kodun okunabilir karşılığını tutan kolon.</summary>
    public const string Label = "label";

    public static readonly string[] AllKinds = [Relationship, Synonym, Meaning, CodeMeaning, Label];

    /// <summary>
    /// Ayni seyin iki kez sorulmasini engelleyen kimlik.
    ///
    /// Normalize edilmis (kucuk harf, nitelenmis ad) oldugu icin
    /// <c>dbo.Musteriler</c> ile <c>DBO.MUSTERILER</c> ayni anahtara duser.
    /// Fabrika metotlarindan uretiliyor; elle yazilmiyor ki icerikle
    /// anahtar birbirinden ayrilamasin.
    /// </summary>
    public required string Key { get; init; }

    public required string Kind { get; init; }

    /// <summary>
    /// Kullanici bu bilgiyi onayladi mi.
    ///
    /// <c>false</c> kaydi da saklanir ve bu SART: reddedilen bilgi
    /// saklanmazsa sistem ayni yanlis eslesmeyi her sorguda yeniden kurar ve
    /// yeniden sorar. "Bir daha sormayalim" sozu ancak <em>hayir</em> cevabi
    /// da hatirlanirsa tutulabilir.
    /// </summary>
    public required bool Accepted { get; init; }

    /* ── Iliski alanlari ── */
    public string? FromTable { get; init; }
    public string? FromColumn { get; init; }
    public string? ToTable { get; init; }
    public string? ToColumn { get; init; }

    /* ── Es anlamli / kod anlami / etiket alanlari ── */
    public string? Table { get; init; }
    public string? Column { get; init; }

    /// <summary>Kod anlamında kodun kendisi ("ROD").</summary>
    public string? Value { get; init; }

    /// <summary>Kullanıcının verdiği karşılık ("karayolu", "gelir").</summary>
    public string? Means { get; init; }

    /* ── Denetim izi ──
       "Kim, ne zaman, hangi soruya cevaben" — yanlis ogrenilmis bir bilgi
       bulundugunda tek tutamak bu. */
    public string? Question { get; init; }
    public string? UserId { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Kullanicinin okuyacagi tek satirlik ozet. "Ogrendiklerim" ekraninda
    /// gosterilen sey bu; teknik kimlik degil, cumle.
    /// </summary>
    public string Describe() => Kind switch
    {
        Relationship => $"{FromTable}.{FromColumn} → {ToTable}.{ToColumn}",
        Synonym => $"“{Means}” → {Qualify(Table, Column)}",
        Meaning => $"{Qualify(Table, Column)}: {Means}",
        CodeMeaning => $"{Qualify(Table, Column)} = “{Value}” → {Means}",
        Label => $"{Table} için etiket kolonu: {Column}",
        _ => Key,
    };

    private static string Qualify(string? table, string? column) =>
        string.IsNullOrWhiteSpace(column) ? table ?? "" : $"{table}.{column}";

    /* ── Fabrikalar ───────────────────────────────────────────────────── */

    public static LearnedFact ForRelationship(
        string fromTable, string fromColumn, string toTable, string toColumn,
        bool accepted, string? question = null, string? userId = null) => new()
        {
            Key = RelationshipKey(fromTable, fromColumn, toTable, toColumn),
            Kind = Relationship,
            Accepted = accepted,
            FromTable = fromTable,
            FromColumn = fromColumn,
            ToTable = toTable,
            ToColumn = toColumn,
            Question = question,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

    public static LearnedFact ForSynonym(
        string table, string? column, string means,
        bool accepted = true, string? question = null, string? userId = null) => new()
        {
            Key = $"syn:{Norm(table)}.{Norm(column)}={Norm(means)}",
            Kind = Synonym,
            Accepted = accepted,
            Table = table,
            Column = column,
            Means = means,
            Question = question,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

    /// <summary>
    /// Bir tablonun ya da kolonun tanimi. Anahtar TANIMI icermiyor: ayni
    /// kolon yeniden tanimlanirsa yeni satir acilmamali, ustune yazilmali.
    /// </summary>
    public static LearnedFact ForMeaning(
        string table, string? column, string means,
        bool accepted = true, string? question = null, string? userId = null) => new()
        {
            Key = $"mean:{Norm(table)}.{Norm(column)}",
            Kind = Meaning,
            Accepted = accepted,
            Table = table,
            Column = column,
            Means = means,
            Question = question,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

    public static LearnedFact ForCodeMeaning(
        string table, string column, string value, string means,
        bool accepted = true, string? question = null, string? userId = null) => new()
        {
            Key = $"code:{Norm(table)}.{Norm(column)}={Norm(value)}",
            Kind = CodeMeaning,
            Accepted = accepted,
            Table = table,
            Column = column,
            Value = value,
            Means = means,
            Question = question,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

    public static LearnedFact ForLabel(
        string table, string column,
        bool accepted = true, string? question = null, string? userId = null) => new()
        {
            Key = $"label:{Norm(table)}",
            Kind = Label,
            Accepted = accepted,
            Table = table,
            Column = column,
            Question = question,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

    /// <summary>
    /// Iliski anahtari. Yon KORUNUYOR: <c>a.x -> b.y</c> ile <c>b.y -> a.x</c>
    /// ayni sey degil — ilki "a'nin her satiri b'den birini gosterir" demek,
    /// tersi bambaska bir iddia ve satirlari cogaltabilir.
    /// </summary>
    public static string RelationshipKey(
        string fromTable, string fromColumn, string toTable, string toColumn) =>
        $"rel:{Norm(fromTable)}.{Norm(fromColumn)}->{Norm(toTable)}.{Norm(toColumn)}";

    /// <summary>
    /// Anahtar icin ad normalizasyonu. Koseli parantezler BASTAN SONA
    /// atiliyor, uctan kirpilmiyor: <c>[dbo].[Musteriler]</c> ortadaki
    /// parantezleri de tasiyor ve kirpma onu <c>dbo].[musteriler</c> yapar —
    /// yani ayni tablo iki ayri anahtara duser ve "bir daha sorma" bozulur.
    /// </summary>
    private static string Norm(string? value) =>
        string.Concat((value ?? "").Where(c => c is not ('[' or ']')))
            .Trim()
            .ToLowerInvariant();
}

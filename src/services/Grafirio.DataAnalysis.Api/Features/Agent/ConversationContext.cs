using System.Text;
using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

/// <summary>
/// Konusmanin gecmisi — zincirin toplanmasi ve ceviri prompt'una cevrilmesi.
///
/// Sorun tek bir eksiklikten degil, uc ayri bosluktan geliyordu: arayuz takip
/// sorusunu bir oncekine baglamiyordu, sistem soru sordugunu hicbir yere
/// yazmiyordu ve ceviriye gecmis verilmiyordu. Ilk ikisi
/// <see cref="QueryHistory.ParentQueryId"/> ve <c>clarification</c> durumuyla
/// kapandi; ucuncusu burasi.
///
/// Sinif ikiye ayrilmis: <see cref="LoadAsync"/> veritabanina gider,
/// <see cref="Render"/> saf fonksiyondur. Bolme testin hatiri icin — prompt'a
/// neyin girdigi gozle dogrulanabilen bir sey degil ve yanlis oldugunda model
/// sessizce baska bir soruyu cevaplar.
/// </summary>
public static class ConversationContext
{
    /// <summary>
    /// Prompt'a girecek en fazla tur sayisi.
    ///
    /// Ust sinir bir performans onlemi degil: eski bir yanlis anlamanin
    /// sonraki turlara sizmasi (baglam zehirlenmesi) gercek bir risk ve zincir
    /// uzadikca artiyor. Alti tur, "sistem sordu -> kullanici cevapladi"
    /// dongusunun uc kez tekrarlanmasina yetiyor.
    /// </summary>
    public const int MaxTurns = 6;

    /// <summary>Bir turun prompt'a girecek kadari.</summary>
    public sealed record Turn(
        string Question,
        string Status,
        string? ClarificationQuestion,
        string? AnalysisJson,
        string? Answer);

    /// <summary>
    /// <paramref name="parentQueryId"/>'den geriye dogru yuruyerek zinciri
    /// toplar; sonucu eskiden yeniye siralar.
    ///
    /// Kapsam <paramref name="allowedConfigIds"/> ile sinirli: zincir baska bir
    /// baglantiya ya da baska bir sirketin verisine sicramaz. Bir adim bu
    /// kumenin disina cikarsa yuruyus orada biter — sessizce, cunku
    /// kullanicinin gorecegi bir hata degil, tasinmayacak bir baglamdir.
    /// </summary>
    public static async Task<List<Turn>> LoadAsync(
        DataAnalysisDbContext db,
        Guid? parentQueryId,
        IReadOnlyCollection<Guid> allowedConfigIds,
        CancellationToken ct = default)
    {
        var turns = new List<Turn>();
        if (parentQueryId is null || allowedConfigIds.Count == 0) return turns;

        // Bozuk bir ebeveyn zinciri (kendini gosteren ya da donen kayit)
        // sonsuz donguye girmesin. Kayitlar tek yonlu yazildigi icin boyle bir
        // sey beklenmiyor; beklenmeyen sey de sunucuyu kilitlememeli.
        var seen = new HashSet<Guid>();
        var cursor = parentQueryId;

        while (cursor is not null && turns.Count < MaxTurns && seen.Add(cursor.Value))
        {
            var id = cursor.Value;
            var row = await db.QueryHistories
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == id, ct);

            if (row is null || !allowedConfigIds.Contains(row.ConfigId)) break;

            turns.Add(new Turn(
                Question: row.Question,
                Status: row.Status,
                ClarificationQuestion: row.ClarificationQuestion,
                AnalysisJson: row.PyCaretParamsJson,
                Answer: ExtractAnswer(row.ResultJson)));

            cursor = row.ParentQueryId;
        }

        turns.Reverse(); // eskiden yeniye: konusma sirasiyla okunmali
        return turns;
    }

    /// <summary>
    /// Turlari ceviri prompt'una girecek metne cevirir. Zincir bossa bos dize
    /// doner ve prompt'ta hic baslik acilmaz.
    /// </summary>
    public static string Render(IReadOnlyList<Turn> turns)
    {
        if (turns.Count == 0) return string.Empty;

        var text = new StringBuilder();
        text.AppendLine("## Önceki konuşma");
        text.AppendLine();
        text.AppendLine(
            "Aşağıdaki turlar bu konuşmada daha önce yaşandı; eskiden yeniye "
            + "sıralı. Kullanıcının şu an yazdığı mesaj YENİ BİR SORU OLMAYABİLİR: "
            + "senin bir önceki turda sorduğun soruya verilmiş kısa bir cevap "
            + "olabilir. Örneğin \"import tablosundan bakman yeterliydi\" başlı "
            + "başına bir soru değildir — önceki turda hangi tabloyu kastettiğini "
            + "sorduğun sorunun cevabıdır. Böyle bir durumda iki mesajı BİRLEŞTİR "
            + "ve asıl soruyu cevapla.");
        text.AppendLine();

        for (var i = 0; i < turns.Count; i++)
        {
            var turn = turns[i];
            var isLast = i == turns.Count - 1;

            text.AppendLine($"### Tur {i + 1}{(isLast ? " (en son)" : string.Empty)}");
            text.AppendLine($"Kullanıcı: \"{Collapse(turn.Question)}\"");

            if (turn.Status == ClarificationStatus)
            {
                // En kritik satir bu: sistemin sordugu soru. Kaydedilmedigi
                // surece kullanicinin cevabinin neye cevap oldugu bilinemezdi.
                text.AppendLine(
                    "Sen sunu SORDUN: \""
                    + Collapse(turn.ClarificationQuestion ?? "(soru kaydedilmemiş)")
                    + "\"");
            }
            else if (turn.Status == "failed")
            {
                text.AppendLine("Bu tur başarısız oldu; sonuç üretilmedi.");
            }
            else
            {
                var summary = DescribeAnalysis(turn.AnalysisJson);
                if (summary.Length > 0)
                    text.AppendLine($"Sen şu analizi ürettin: {summary}");
                if (!string.IsNullOrWhiteSpace(turn.Answer))
                    text.AppendLine($"Sonuç: {Collapse(turn.Answer)}");
            }

            // Son tamamlanmis turun parametreleri BUTUN halde veriliyor: "peki
            // gecen yil?" gibi bir devam sorusu, onceki sorgunun aynisini bir
            // filtre degisikligiyle isteyen sorudur. Ozet yeterli degil; modelin
            // uzerine ekleme yapabilmesi icin tam JSON gerekiyor.
            if (isLast
                && turn.Status != ClarificationStatus
                && !string.IsNullOrWhiteSpace(turn.AnalysisJson)
                && turn.AnalysisJson.Trim() != "{}")
            {
                text.AppendLine("Bu turun tam parametreleri:");
                text.AppendLine("```json");
                text.AppendLine(turn.AnalysisJson.Trim());
                text.AppendLine("```");
            }

            text.AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Sistemin soru sorup bekledigi tur. <see cref="QueryHistory.Status"/>
    /// degeri olarak da, prompt'ta da ayni sabit kullaniliyor.
    /// </summary>
    public const string ClarificationStatus = "clarification";

    /// <summary>
    /// Analiz parametrelerinin bir satirlik ozeti: hangi tablo, hangi kirilim,
    /// hangi olcum. Tam JSON her tur icin fazla yer kaplar ve eski turlarin
    /// ayrintisi devam sorusu icin gerekmiyor.
    /// </summary>
    private static string DescribeAnalysis(string? analysisJson)
    {
        if (string.IsNullOrWhiteSpace(analysisJson)) return string.Empty;

        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(analysisJson);
            if (doc.ValueKind != JsonValueKind.Object) return string.Empty;

            var parts = new List<string>();

            if (TryText(doc, "target_table", out var table)) parts.Add($"tablo {table}");

            if (doc.TryGetProperty("joins", out var joins) && joins.ValueKind == JsonValueKind.Array)
            {
                var joined = joins.EnumerateArray()
                    .Select(j => TryText(j, "table", out var t) ? t : null)
                    .Where(t => t is not null)
                    .ToList();
                if (joined.Count > 0) parts.Add($"bağlanan: {string.Join(", ", joined)}");
            }

            var groups = TextList(doc, "group_by");
            if (groups.Count > 0) parts.Add($"kırılım {string.Join(", ", groups)}");

            if (TryText(doc, "aggregation", out var aggregation) && aggregation != "none")
            {
                parts.Add(TryText(doc, "target_column", out var target)
                    ? $"{aggregation}({target})"
                    : aggregation);
            }

            if (doc.TryGetProperty("filters", out var filters)
                && filters.ValueKind == JsonValueKind.Object)
            {
                var names = filters.EnumerateObject().Select(p => p.Name).ToList();
                if (names.Count > 0) parts.Add($"filtre: {string.Join(", ", names)}");
            }

            return string.Join(" · ", parts);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>Sonuc govdesindeki duzyazi ozet — varsa.</summary>
    private static string? ExtractAnswer(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return null;

        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(resultJson);
            if (doc.ValueKind != JsonValueKind.Object) return null;

            if (TryText(doc, "summary", out var summary)) return summary;
            if (TryText(doc, "answer", out var answer)) return answer;
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryText(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind != JsonValueKind.String) return false;

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text)) return false;

        value = text;
        return true;
    }

    private static List<string> TextList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return array.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    /// <summary>
    /// Satir sonlarini bosluga cevirir ve uzun metni kirpar.
    ///
    /// Kullanici metni prompt'a giriyor: icindeki satir sonu, prompt'un kendi
    /// basliklarini taklit eden bir yapi kurabilir. Tek satira indirmek bunu
    /// engelliyor; kirpma ise butcenin gecmis turlara akmasini.
    /// </summary>
    private static string Collapse(string? text, int maxLength = 400)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var single = text.Replace('\r', ' ').Replace('\n', ' ').Replace('`', '\'').Trim();
        while (single.Contains("  ")) single = single.Replace("  ", " ");

        return single.Length <= maxLength ? single : single[..maxLength] + "…";
    }
}

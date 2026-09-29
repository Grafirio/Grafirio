using System.Text.Json;
using System.Text.Json.Nodes;

namespace Grafirio.Measurement.Evaluation;

/// <summary>
/// Degerlendirme seti dosyasi (<c>evals/*.json</c>). Her vaka bir soru ve o soruya
/// verilmesi beklenen cevabin ozellikleri.
///
/// Beklenti bilerek "alt kume" olarak yaziliyor: LLM'in urettigi parametrelerin
/// tamamini sabitlemek, anlami ayni ama yazimi farkli her cevabi hata sayardi.
/// Yalnizca dogrulugu belirleyen alanlar yaziliyor (hangi tablo, hangi toplama,
/// hangi kirilim).
/// </summary>
public sealed record EvalSuite
{
    public required string Suite { get; init; }
    public string? Description { get; init; }

    /// <summary>Sorularin soruldugu kayitli baglanti. <c>--connection</c> ile ezilebilir.</summary>
    public Guid? ConnectionId { get; init; }

    /// <summary>Her sorunun kac kez sorulacagi — tutarlilik icin en az 2.</summary>
    public int? Repeat { get; init; }

    public List<EvalCase> Cases { get; init; } = [];

    public static EvalSuite Load(string path)
    {
        var suite = JsonSerializer.Deserialize<EvalSuite>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException($"{path} bos.");

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(suite.Suite)) errors.Add("suite gerekli.");
        if (suite.Cases.Count == 0) errors.Add("en az bir vaka gerekli.");

        foreach (var duplicate in suite.Cases.GroupBy(c => c.Name).Where(g => g.Count() > 1))
            errors.Add($"'{duplicate.Key}' adi birden fazla vakada: vaka adlari trendin kimligi.");

        foreach (var item in suite.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Name)) errors.Add("her vakanin adi olmali.");
            if (string.IsNullOrWhiteSpace(item.Question)) errors.Add($"'{item.Name}': question bos.");
            if (item.Expect.Status is not (null or "completed" or "clarification" or "failed"))
                errors.Add($"'{item.Name}': expect.status completed | clarification | failed olmali.");
        }

        if (errors.Count > 0)
            throw new InvalidDataException($"{path} gecersiz:\n  - " + string.Join("\n  - ", errors));

        return suite;
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed record EvalCase
{
    public required string Name { get; init; }
    public string Group { get; init; } = "";
    public required string Question { get; init; }
    public EvalExpectation Expect { get; init; } = new();
}

public sealed record EvalExpectation
{
    /// <summary>
    /// Beklenen son durum. Varsayilan <c>completed</c>. Belirsiz bir soru icin
    /// <c>clarification</c> yazmak, sistemin tahmin etmek yerine SORDUGUNU
    /// dogrular — guvenilirligin en onemli kaniti bu.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// LLM parametrelerinin alt kumesi. Nesneler ic ice karsilastirilir; diziler
    /// "hepsini icersin" anlaminda (sira onemsiz); metinler buyuk/kucuk harf ve
    /// tablo adi koseli parantezinden bagimsiz.
    /// </summary>
    public JsonObject? Params { get; init; }

    /// <summary>Parametrelerde BULUNMAMASI gereken anahtarlar (orn. gereksiz bir join).</summary>
    public List<string>? ParamsAbsent { get; init; }
}

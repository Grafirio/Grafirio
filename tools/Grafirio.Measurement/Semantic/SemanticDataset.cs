using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.Measurement.Evaluation;

namespace Grafirio.Measurement.Semantic;

/// <summary>
/// Semantik degerlendirme veri seti: <c>evals/semantic/&lt;ad&gt;/</c> klasoru.
///
///   schema.sql       semayi kurar
///   seed.sql         belirlenimci veriyi yukler
///   gold.json        dogru yorum: kolon rolleri, hassas kolonlar, tablo kavramlari, iliskiler
///   questions.json   sorular: beklenen parametreler, altin SQL, kabul edilen grafikler
///
/// Hem surec ici degerlendirme (grafirio-semantic) hem canli olcum
/// (grafirio-measure semantic-live) ayni dosyalari okuyor; altin veri tek yerde.
/// </summary>
public sealed class SemanticDataset
{
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public required SemanticGold Gold { get; init; }
    public required SemanticQuestionSet Questions { get; init; }

    public string SchemaFile => Path.Combine(Directory, "schema.sql");
    public string SeedFile => Path.Combine(Directory, "seed.sql");

    /// <summary>Kurulumda kullanilan veritabani adi.</summary>
    public string DatabaseName => $"GrafirioEval_{Name}";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static SemanticDataset Load(string directory)
    {
        var gold = Read<SemanticGold>(Path.Combine(directory, "gold.json"));
        var questionsPath = Path.Combine(directory, "questions.json");
        var questions = File.Exists(questionsPath) ? Read<SemanticQuestionSet>(questionsPath) : new SemanticQuestionSet();

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(gold.Dataset)) errors.Add("gold.json: dataset gerekli.");
        foreach (var (key, column) in gold.Columns)
        {
            if (key.Count(c => c == '.') < 2) errors.Add($"gold.json: kolon anahtari sema.tablo.kolon olmali: {key}");
            if (column.Role.Count == 0) errors.Add($"gold.json: {key} icin en az bir rol gerekli.");
        }
        foreach (var edge in gold.Relationships.Concat(gold.NonRelationships))
            if (edge.From.Count(c => c == '.') < 2 || edge.To.Count(c => c == '.') < 2)
                errors.Add($"gold.json: iliski uclari sema.tablo.kolon olmali: {edge.From} -> {edge.To}");
        foreach (var duplicate in questions.Questions.GroupBy(q => q.Id).Where(g => g.Count() > 1))
            errors.Add($"questions.json: '{duplicate.Key}' kimligi tekrar ediyor.");
        foreach (var question in questions.Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Question)) errors.Add($"questions.json: {question.Id} sorusu bos.");
            if (question.Kind is not ("answerable" or "unknown"))
                errors.Add($"questions.json: {question.Id} kind answerable | unknown olmali.");
            if (question.Kind == "unknown" && question.Expect.Status is not ("clarification" or "failed"))
                errors.Add($"questions.json: {question.Id} bilinmeyen soru; expect.status clarification olmali.");
        }

        if (errors.Count > 0)
            throw new InvalidDataException($"{directory} gecersiz:\n  - " + string.Join("\n  - ", errors));

        return new SemanticDataset
        {
            Name = gold.Dataset,
            Directory = directory,
            Gold = gold,
            Questions = questions
        };
    }

    /// <summary><c>evals/semantic</c> altindaki butun veri setleri (ya da verilen adlar).</summary>
    public static List<SemanticDataset> LoadAll(string root, IReadOnlyCollection<string>? only = null) =>
        System.IO.Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "gold.json")))
            .Where(d => only is null || only.Count == 0 || only.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            .OrderBy(d => d, StringComparer.Ordinal)
            .Select(Load)
            .ToList();

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"{path} bos.");
}

public sealed record SemanticGold
{
    public string Dataset { get; init; } = "";
    public string? Description { get; init; }
    public Dictionary<string, GoldTable> Tables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, GoldColumn> Columns { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GoldEdge> Relationships { get; init; } = [];
    public List<GoldEdge> NonRelationships { get; init; } = [];
}

public sealed record GoldTable
{
    public List<string> Concepts { get; init; } = [];
    public List<string> Forbidden { get; init; } = [];
}

public sealed record GoldColumn
{
    /// <summary>Kabul edilen roller; ilki tercih edilen.</summary>
    public List<string> Role { get; init; } = [];
    public bool Sensitive { get; init; }
}

public sealed record GoldEdge
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Kind { get; init; } = "";
    public string? Note { get; init; }
    public string? Reason { get; init; }
}

public sealed record SemanticQuestionSet
{
    public List<SemanticQuestion> Questions { get; init; } = [];
}

/// <summary>
/// Bir soru. <c>answerable</c> sorular esleme, SQL ve grafik icin; <c>unknown</c>
/// sorular sistemin uydurmak yerine sormasi icin. Cevaplanabilir sorular ayni
/// zamanda "gereksiz yere sordu mu" olcumunun kontrol grubu.
/// </summary>
public sealed record SemanticQuestion
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "answerable";
    public string Group { get; init; } = "";
    public string Question { get; init; } = "";

    /// <summary>Ayni niyetin baska yazilislari — ifade tutarliligi icin.</summary>
    public List<string> Paraphrases { get; init; } = [];

    /// <summary>Bilinmeyen sorularda: missing-column | missing-table | ambiguous | out-of-scope | sensitive.</summary>
    public string? UnknownType { get; init; }

    public EvalExpectation Expect { get; init; } = new();
    public GoldAnswer? Gold { get; init; }
}

public sealed record GoldAnswer
{
    /// <summary>Test veritabaninda calistirilip sonucu sistemin sonucuyla karsilastirilan sorgu.</summary>
    public string? Sql { get; init; }

    /// <summary>Kabul edilen grafik turleri (bar, line, pie, doughnut, scatter, number...).</summary>
    public List<string> Charts { get; init; } = [];

    /// <summary>Satir sirasi anlamli mi (en cok 5, zaman serisi).</summary>
    public bool Ordered { get; init; }
}

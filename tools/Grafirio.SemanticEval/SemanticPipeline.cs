using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Measurement.Semantic;
using Grafirio.QueryPolicy;
using Grafirio.SemanticEval.Data;
using Grafirio.SemanticEval.Scoring;
using Microsoft.Extensions.Logging;

namespace Grafirio.SemanticEval;

/// <summary>Bir veri seti icin uretilmis sozluk: kanonik JSON + modelin ozeti.</summary>
public sealed record DictionaryArtifact(string Json, string Summary);

/// <summary>
/// DataAnalysis'in "Analiz Et" ve soru cevirisi adimlarini, uretimdeki SIRAYLA
/// ve ayni siniflarla calistirir (ConnectionAnalysisConsumer + AgentQueryEndpoints).
/// Kopya mantik yok: olculen sey uretimde calisan kodun kendisi.
///
/// Uretimden bilincli farklar:
///   * Baglanti: test veritabanina Windows kimligiyle (bkz. EvalSqlSession).
///   * Ogrenilmis bilgi (LearnedFacts) yok: sistemin kullanici yardimi olmadan
///     ne yaptigi olculuyor.
///   * Analiz sorulari cevaplanmiyor: ayni sebep.
///   * Soru cevirisi PyCaret'e gonderilmiyor; SQL ve grafik canli olcumde.
/// </summary>
public sealed class SemanticPipeline(ILlmClient llm, ILoggerFactory loggers, string server)
{
    private readonly LlmAnalysisService _analysis = new(llm, loggers.CreateLogger<LlmAnalysisService>());

    public bool LlmAvailable => llm.IsConfigured;

    /// <summary>Profil sirasinda alinan (ve kesif kodunca yutulan) SQL hatalari, veri seti basina.</summary>
    public Dictionary<string, List<string>> SqlErrors { get; } = [];

    /// <summary>Profil + iliski kesfi. Ornekleme onayi VERILMIS kabul ediliyor (deger ortusmesi icin sart).</summary>
    public async Task<DatabaseProfile> ProfileAsync(SemanticDataset dataset, CancellationToken ct)
    {
        var profiler = new SchemaProfiler(loggers.CreateLogger<SchemaProfiler>(),
            new RelationshipDiscovery(loggers.CreateLogger<RelationshipDiscovery>()));

        await using var session = await EvalSqlSession.OpenAsync(
            DatasetInstaller.ConnectionString(server, dataset.DatabaseName), ct);

        var profile = await profiler.ProfileAsync(session, dataset.DatabaseName, SelectedTables(dataset),
            samplingConsentGiven: true, ct: ct);

        // Kesif kodu bazi SQL hatalarini "aday elendi" diye yutuyor. Yutulan her
        // hata burada toplanip rapora yaziliyor: sessiz bir hata, dusuk bir
        // recall rakamindan cok daha net bir teshis.
        SqlErrors[dataset.Name] = session.SqlErrors.ToList();
        return profile;
    }

    /// <summary>
    /// Semantik sozluk — tuketicideki adimlarin aynisi: parcala, her parcayi
    /// modele sor, birlestir, kanonik bicime getir, profil gerceklerini ekle.
    /// </summary>
    public async Task<DictionaryArtifact> BuildDictionaryAsync(DatabaseProfile profile, CancellationToken ct)
    {
        var options = ConnectionAnalysisConsumer.JsonOptions;
        var chunks = DictionaryChunks.Split(profile, chunk => PromptProfile.Serialize(chunk, options).Length);
        var result = await _analysis.BuildSchemaDictionaryAsync(
            chunks.Select(c => PromptProfile.Serialize(c, options)).ToList(),
            profile.Tables.Select(t => t.Qualified).ToList(), ct: ct);

        if (!result.Success)
            throw new InvalidOperationException($"Sozluk uretilemedi: {result.Error}");

        var canonical = CanonicalSchemaDictionary.Build(result.Json!, profile);
        var json = ConnectionAnalysisConsumer.AttachProfileFacts(canonical.ToJsonString(options), profile, []);
        return new DictionaryArtifact(json, result.Explanation ?? "");
    }

    /// <summary>
    /// Soruyu cevirir ve SubmitQuery'nin verdigi karari verir: netlestirme mi,
    /// kapsam reddi mi, yoksa calistirilabilir bir sorgu mu.
    /// </summary>
    public async Task<TranslationOutcome> TranslateAsync(SemanticDataset dataset, DictionaryArtifact dictionary,
        string text, CancellationToken ct)
    {
        var translation = await _analysis.TranslateQuestionAsync(text, dictionary.Json, dictionary.Summary, "", ct);
        if (!translation.Success)
            return new TranslationOutcome(text, "failed", null, translation.Error);

        JsonNode? parameters;
        try { parameters = JsonNode.Parse(translation.Json!); }
        catch (JsonException exception) { return new TranslationOutcome(text, "failed", null, exception.Message); }

        try
        {
            AgentQueryTableScope.Validate(translation.Json!, dictionary.Json, SelectedTables(dataset));
            var proposals = RelationshipDictionary.ReadProposals(translation.Json!, dictionary.Json);
            var target = parameters?["target_table"] is JsonValue value && value.TryGetValue<string>(out var t)
                ? t : null;

            return string.IsNullOrWhiteSpace(target) || proposals.Count > 0
                ? new TranslationOutcome(text, "clarification", parameters, null)
                : new TranslationOutcome(text, "completed", parameters, null);
        }
        catch (Exception exception) when (exception is QueryPolicyException or ArgumentException)
        {
            return new TranslationOutcome(text, "rejected", parameters, exception.Message);
        }
    }

    /// <summary>Kullanicinin "sectigi" tablolar: altin verideki butun tablolar.</summary>
    public static List<string> SelectedTables(SemanticDataset dataset) => dataset.Gold.Tables.Keys.ToList();
}

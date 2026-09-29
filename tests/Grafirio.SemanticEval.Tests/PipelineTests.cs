using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Measurement.Semantic;
using Grafirio.SemanticEval.Data;
using Grafirio.SemanticEval.Scoring;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.SemanticEval.Tests;

/// <summary>
/// Hattin gercek veritabanina karsi uctan uca calismasi: kurulum, profil, iliski
/// kesfi, sozluk ve ceviri kararlari. LLM sahte (belirlenimci cevap); olculen
/// sey LLM'in kalitesi degil, hattin uretim koduyla dogru kararlari vermesi.
///
/// LocalDB yoksa (Windows disi CI) testler bir sey yapmadan doner ve bunu
/// ciktiya yazar.
/// </summary>
public class PipelineTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "evals", "semantic");

    private static async Task<bool> LocalDbAvailable()
    {
        try
        {
            await using var connection = new SqlConnection(
                DatasetInstaller.ConnectionString(DatasetInstaller.DefaultServer, "master"));
            await connection.OpenAsync();
            return true;
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or PlatformNotSupportedException)
        {
            Console.WriteLine($"LocalDB yok, test atlandi: {exception.Message}");
            return false;
        }
    }

    [Fact]
    public async Task Eticaret_kurulur_iliskiler_bulunur_ceviri_kararlari_uretim_koduyla_verilir()
    {
        if (!await LocalDbAvailable()) return;

        // Ayri bir ad: gelistiricinin kurdugu veritabanini ezmesin.
        var dataset = SemanticDataset.Load(Path.Combine(Root, "eticaret"));
        dataset = new SemanticDataset
        {
            Name = "eticaret_test", Directory = dataset.Directory, Gold = dataset.Gold, Questions = dataset.Questions
        };
        await DatasetInstaller.InstallAsync(dataset, DatasetInstaller.DefaultServer, CancellationToken.None);

        var pipeline = new SemanticPipeline(new FakeLlm(), NullLoggerFactory.Instance, DatasetInstaller.DefaultServer);
        var profile = await pipeline.ProfileAsync(dataset, CancellationToken.None);

        // Butun iliskiler FK olarak bildirilmis: kusursuz olmali.
        var relationships = RelationshipScoring.Score(dataset.Name, dataset.Gold, profile.Relationships);
        Assert.Equal(dataset.Gold.Relationships.Count, relationships.TruePositives);
        Assert.Equal(0, relationships.FalsePositives);

        // Hassas kolonlar profilde korunuyor (Email, FirstName, LastName, Phone, BirthDate, Street).
        var schema = SchemaScoring.Score(dataset.Name, dataset.Gold, profile, null);
        Assert.Equal(1.0, schema.Metrics.Single(m => m.Name == "sensitive.recall").Value);

        var dictionary = await pipeline.BuildDictionaryAsync(profile, CancellationToken.None);
        var root = JsonNode.Parse(dictionary.Json)!.AsObject();
        Assert.NotNull(root["relationships"]);
        Assert.Contains(root["columns"]!.AsArray(), c => c!["column"]!.GetValue<string>() == "TotalAmount"
                                                         && c["role"]!.GetValue<string>() == "measure");

        var completed = await pipeline.TranslateAsync(dataset, dictionary, "toplam siparis", CancellationToken.None);
        Assert.Equal("completed", completed.Status);

        var asked = await pipeline.TranslateAsync(dataset, dictionary, "belirsiz soru", CancellationToken.None);
        Assert.Equal("clarification", asked.Status);

        // Sozlukte olmayan tablo: uretimdeki kapsam dogrulamasi reddetmeli.
        var invented = await pipeline.TranslateAsync(dataset, dictionary, "uydurma tablo", CancellationToken.None);
        Assert.Equal("rejected", invented.Status);
    }

    /// <summary>Sozluk ve ceviri istemlerine sabit cevap veren LLM.</summary>
    private sealed class FakeLlm : ILlmClient
    {
        public bool IsConfigured => true;

        public Task<string> GenerateAsync(string prompt, double temperature = 0.2, int maxTokens = 2048,
            CancellationToken cancellationToken = default)
        {
            if (!prompt.Contains("## Kullanıcının sorusu", StringComparison.Ordinal))
                return Task.FromResult("""
                    ```json
                    {
                      "sector": "e-ticaret", "sectorConfidence": "high",
                      "tables": [ { "name": "dbo.Orders", "purpose": "Siparişler", "synonyms": ["sipariş"], "confidence": "high" } ],
                      "columns": [ { "table": "dbo.Orders", "column": "TotalAmount", "role": "measure",
                                     "meaning": "Sipariş toplam tutarı", "synonyms": ["ciro"], "confidence": "high" } ],
                      "questions": []
                    }
                    ```
                    ### Açıklama
                    Siparişler tablosu.
                    """);

            var json = prompt.Contains("\"toplam siparis\"", StringComparison.Ordinal)
                ? """{ "analysis_type": "aggregation", "target_table": "dbo.Orders", "aggregation": "count", "group_by": [], "chart_type": "bar" }"""
                : prompt.Contains("\"uydurma tablo\"", StringComparison.Ordinal)
                    ? """{ "analysis_type": "aggregation", "target_table": "dbo.Employees", "aggregation": "count", "chart_type": "bar" }"""
                    : """{ "analysis_type": "aggregation", "target_table": "", "description": "Hangisini kastettiniz?" }""";
            return Task.FromResult($"```json\n{json}\n```");
        }
    }
}

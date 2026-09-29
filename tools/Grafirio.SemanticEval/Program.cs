using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Services;
using Grafirio.Measurement.Cli;
using Grafirio.Measurement.Reporting;
using Grafirio.Measurement.Semantic;
using Grafirio.SemanticEval;
using Grafirio.SemanticEval.Data;
using Grafirio.SemanticEval.Scoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

const string Help = """
    grafirio-semantic — semantik zeka degerlendirmesi (surec ici, katman katman)

    Komutlar:
      setup           Veri setlerinin test veritabanlarini sifirdan kurar (LocalDB).
      relationships   Relationship Discovery   (LLM gerekmez)
      schema          Schema Understanding     (hassas kolon kismi LLM'siz; roller/amaclar LLM ister)
      mapping         Semantic Mapping + Unknown Data Handling (LLM ister)
      all             relationships + schema + mapping

    Secenekler:
      --root <klasor>        Veri setleri (varsayilan evals/semantic)
      --datasets a,b         Yalnizca bu veri setleri
      --server <sunucu>      SQL Server (varsayilan "(localdb)\MSSQLLocalDB", Windows kimligi)
      --sql-user <kullanici> SQL kimligi (verilmezse Windows kimligi)
      --sql-password <parola>                                                 [SEMANTIC_SQL_PASSWORD]
      --setup                Olcumden once veritabanlarini yeniden kur
      --reuse-dictionary     Onceki kosunun sozlugunu kullan (LLM maliyeti yok; yalnizca eslemeyi olcer)
      --out-dir <klasor>     Sonuc klasoru (varsayilan artifacts/olcum/semantic)
      --publish <url>        Dashboard'a gonder                               [MEASURE_DASHBOARD_URL]
      --publish-key <key>                                                     [MEASURE_DASHBOARD_KEY]
      --label <metin>

    LLM: AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_DEPLOYMENT, AZURE_OPENAI_API_KEY ortam degiskenleri
    (DataAnalysis ile ayni). Tanimsizsa LLM isteyen olcumler atlanir ve bu raporda yazar.
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Help);
    return 0;
}

// Uretim (container) kulturu invariant. Bu makine tr-TR ise RegexOptions.IgnoreCase
// Turkce buyuk/kucuk harf kurallariyla calisiyor ("I" != "i") ve hassas kolon
// desenleri ("national_?id") "DriverNationalId"i kaciriyor. Olculen sey uretim
// davranisi olmali, gelistiricinin makinesi degil.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

var command = args[0];
var cli = new CommandLine(args.Skip(1));
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

try
{
    var root = cli.Get("root") ?? Path.Combine("evals", "semantic");
    var only = cli.Get("datasets")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var server = cli.Get("server") ?? DatasetInstaller.DefaultServer;
    DatasetInstaller.Credentials = (cli.Get("sql-user"), cli.Get("sql-password", "SEMANTIC_SQL_PASSWORD"));
    var setup = cli.Flag("setup");
    var reuse = cli.Flag("reuse-dictionary");
    var outDir = cli.Get("out-dir") ?? Path.Combine("artifacts", "olcum", "semantic");
    var publish = cli.Get("publish", "MEASURE_DASHBOARD_URL");
    var publishKey = cli.Get("publish-key", "MEASURE_DASHBOARD_KEY");
    var label = cli.Get("label");
    var unknown = cli.Unknown.ToList();
    if (unknown.Count > 0) throw new UsageException("Bilinmeyen secenek: " + string.Join(", ", unknown.Select(u => "--" + u)));

    var datasets = SemanticDataset.LoadAll(root, only);
    if (datasets.Count == 0) throw new UsageException($"{root} altinda veri seti yok.");

    using var loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true)
        .SetMinimumLevel(LogLevel.Warning));
    var pipeline = new SemanticPipeline(CreateLlm(loggers), loggers, server);
    var run = new EvalRun(pipeline, datasets, server, outDir, publish, publishKey, label, reuse);

    switch (command)
    {
        case "setup":
            await run.SetupAsync(cancel.Token);
            return 0;
        case "check-gold":
            if (setup) await run.SetupAsync(cancel.Token);
            return await run.CheckGoldAsync(cancel.Token) ? 0 : 1;
        case "relationships":
            if (setup) await run.SetupAsync(cancel.Token);
            await run.RelationshipsAsync(cancel.Token);
            return 0;
        case "schema":
            if (setup) await run.SetupAsync(cancel.Token);
            await run.SchemaAsync(cancel.Token);
            return 0;
        case "mapping":
            if (setup) await run.SetupAsync(cancel.Token);
            await run.MappingAsync(cancel.Token);
            return 0;
        case "all":
            if (setup) await run.SetupAsync(cancel.Token);
            await run.RelationshipsAsync(cancel.Token);
            await run.SchemaAsync(cancel.Token);
            await run.MappingAsync(cancel.Token);
            return 0;
        default:
            Console.Error.WriteLine($"Bilinmeyen komut: {command}\n");
            Console.WriteLine(Help);
            return 2;
    }
}
catch (UsageException exception)
{
    Console.Error.WriteLine($"Hata: {exception.Message}");
    return 2;
}
catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
                                      or Microsoft.Data.SqlClient.SqlException)
{
    Console.Error.WriteLine($"Hata: {exception.Message}");
    return 1;
}

static ILlmClient CreateLlm(ILoggerFactory loggers)
{
    // LlmClient ayarlari once ortam degiskeninden okuyor (AZURE_OPENAI_*);
    // yapilandirma yalnizca ApiVersion gibi varsayilanlar icin.
    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
    return new LlmClient(new SimpleHttpClientFactory(), configuration, loggers.CreateLogger<LlmClient>());
}

namespace Grafirio.SemanticEval
{
    internal sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        // LLM sozluk cagrilari dakikalar surebiliyor; uretimdeki adli istemci de 10 dk.
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <summary>Komutlarin ortak akisi: olc, puanla, belgeyi yaz, istenirse gonder.</summary>
    internal sealed class EvalRun(SemanticPipeline pipeline, List<SemanticDataset> datasets, string server,
        string outDir, string? publish, string? publishKey, string? label, bool reuseDictionary)
    {
        private readonly Dictionary<string, Grafirio.DataAnalysis.Api.Features.Profile.DatabaseProfile> _profiles = [];
        private readonly Dictionary<string, DictionaryArtifact?> _dictionaries = [];

        public async Task SetupAsync(CancellationToken ct)
        {
            foreach (var dataset in datasets)
            {
                Console.WriteLine($"[setup] {dataset.Name} → {dataset.DatabaseName} ({server})");
                await DatasetInstaller.InstallAsync(dataset, server, ct);
            }
        }

        /// <summary>
        /// Altin veriyi dogrular: her altin SQL calisiyor ve bos donmuyor mu, altin
        /// verideki her tablo/kolon veritabaninda var mi. Veri seti yazarken ilk
        /// calistirilacak komut: yanlis altin veri, dogru sistemi hatali gosterir.
        /// </summary>
        public async Task<bool> CheckGoldAsync(CancellationToken ct)
        {
            var ok = true;
            foreach (var dataset in datasets)
            {
                await using var session = await EvalSqlSession.OpenAsync(
                    DatasetInstaller.ConnectionString(server, dataset.DatabaseName), ct);

                var columns = (await session.QueryRowsAsync(
                        "SELECT s.name + '.' + t.name + '.' + c.name AS k FROM sys.columns c " +
                        "JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id",
                        ct: ct))
                    .Select(r => r.GetString("k")!).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var key in dataset.Gold.Columns.Keys.Concat(dataset.Gold.Relationships.SelectMany(e => new[] { e.From, e.To })))
                {
                    if (columns.Contains(key)) continue;
                    ok = false;
                    Console.WriteLine($"[check-gold] {dataset.Name}: altin veride olan kolon veritabaninda yok: {key}");
                }

                var missing = columns.Where(c => !dataset.Gold.Columns.ContainsKey(c)).ToList();
                if (missing.Count > 0)
                    Console.WriteLine($"[check-gold] {dataset.Name}: altin veride rolu olmayan kolonlar: {string.Join(", ", missing)}");

                foreach (var question in dataset.Questions.Questions.Where(q => q.Gold?.Sql is not null))
                {
                    try
                    {
                        var rows = await session.QueryRowsAsync(question.Gold!.Sql!, ct: ct);
                        var preview = string.Join(" | ", rows.Take(3).Select(r => string.Join(", ", r.Values.Values)));
                        Console.WriteLine($"[check-gold] {dataset.Name} {question.Id}: {rows.Count} satir — {preview}");
                        if (rows.Count == 0 || rows.All(r => r.Values.Values.All(v => v is null)))
                        {
                            ok = false;
                            Console.WriteLine("             ↑ bos sonuc: soru bu veride olculemez.");
                        }
                    }
                    catch (Grafirio.DataAnalysis.Api.Data.Access.DataSourceException exception)
                    {
                        ok = false;
                        Console.WriteLine($"[check-gold] {dataset.Name} {question.Id}: HATA {exception.Message}");
                    }
                }
            }

            Console.WriteLine(ok ? "[check-gold] Altin veri gecerli." : "[check-gold] Altin veride sorun var.");
            return ok;
        }

        public async Task RelationshipsAsync(CancellationToken ct)
        {
            var started = DateTimeOffset.UtcNow;
            var results = new List<RelationshipScoring.DatasetResult>();
            foreach (var dataset in datasets)
            {
                var profile = await ProfileAsync(dataset, ct);
                var result = RelationshipScoring.Score(dataset.Name, dataset.Gold, profile.Relationships);
                results.Add(result);
                Console.WriteLine($"[relationships] {dataset.Name}: {result.TruePositives} dogru, " +
                                  $"{result.FalsePositives} yanlis pozitif, {result.FalseNegatives} kacan, " +
                                  $"{result.TrapHits} tuzak");
            }

            await FinishAsync("semantic.relationships", started, RelationshipScoring.Summarize(results),
                results.SelectMany(r => r.Cases).ToList(), [], ct);
        }

        public async Task SchemaAsync(CancellationToken ct)
        {
            var started = DateTimeOffset.UtcNow;
            var results = new List<SchemaScoring.DatasetResult>();
            foreach (var dataset in datasets)
            {
                var profile = await ProfileAsync(dataset, ct);
                var dictionary = await DictionaryAsync(dataset, profile, ct);
                results.Add(SchemaScoring.Score(dataset.Name, dataset.Gold, profile,
                    dictionary is null ? null : JsonNode.Parse(dictionary.Json)!.AsObject()));
            }

            await FinishAsync("semantic.schema", started, SchemaScoring.Summarize(results),
                results.SelectMany(r => r.Cases).ToList(),
                pipeline.LlmAvailable || reuseDictionary ? [] : ["LLM tanimsiz: yalnizca hassas kolon korumasi olculdu."], ct);
        }

        public async Task MappingAsync(CancellationToken ct)
        {
            var started = DateTimeOffset.UtcNow;
            var items = new List<(string, SemanticQuestion, List<TranslationOutcome>)>();

            foreach (var dataset in datasets)
            {
                if (!pipeline.LlmAvailable)
                {
                    Console.WriteLine($"[mapping] {dataset.Name}: LLM tanimsiz, atlandi.");
                    continue;
                }

                var profile = await ProfileAsync(dataset, ct);
                var dictionary = await DictionaryAsync(dataset, profile, ct);
                if (dictionary is null) continue;

                foreach (var question in dataset.Questions.Questions)
                {
                    var outcomes = new List<TranslationOutcome>();
                    foreach (var text in new[] { question.Question }.Concat(question.Paraphrases))
                    {
                        var outcome = await pipeline.TranslateAsync(dataset, dictionary, text, ct);
                        outcomes.Add(outcome);
                        Console.WriteLine($"[mapping] {dataset.Name} {question.Id} {outcome.Status,-13} {text}");
                    }
                    items.Add((dataset.Name, question, outcomes));
                }
            }

            if (items.Count == 0)
            {
                Console.WriteLine("[mapping] Olculecek soru yok (LLM tanimsiz ya da questions.json bos).");
                return;
            }

            var scored = MappingScoring.Score(items);
            await FinishAsync("semantic.mapping", started, scored.MappingMetrics, scored.MappingCases, [], ct);
            await FinishAsync("semantic.unknown", started, scored.UnknownMetrics, scored.UnknownCases, [], ct);
        }

        private async Task<Grafirio.DataAnalysis.Api.Features.Profile.DatabaseProfile> ProfileAsync(
            SemanticDataset dataset, CancellationToken ct)
        {
            if (_profiles.TryGetValue(dataset.Name, out var cached)) return cached;
            try
            {
                return _profiles[dataset.Name] = await pipeline.ProfileAsync(dataset, ct);
            }
            catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 4060 or 911)
            {
                throw new InvalidOperationException(
                    $"{dataset.DatabaseName} yok. Once 'grafirio-semantic setup' calistirin ya da --setup ekleyin.");
            }
        }

        /// <summary>
        /// Sozluk bir kez uretilip saklanir: esleme olcumu onu tekrar tekrar
        /// kullanir ve her uretim LLM parasi demek. --reuse-dictionary ile
        /// onceki kosunun sozlugu okunur (esleme degisikliklerini olcmek icin).
        /// </summary>
        private async Task<DictionaryArtifact?> DictionaryAsync(SemanticDataset dataset,
            Grafirio.DataAnalysis.Api.Features.Profile.DatabaseProfile profile, CancellationToken ct)
        {
            if (_dictionaries.TryGetValue(dataset.Name, out var cached)) return cached;
            var path = Path.Combine(outDir, $"{dataset.Name}-dictionary.json");

            DictionaryArtifact? artifact = null;
            if (reuseDictionary && File.Exists(path))
            {
                artifact = JsonSerializer.Deserialize<DictionaryArtifact>(await File.ReadAllTextAsync(path, ct));
                Console.WriteLine($"[schema] {dataset.Name}: onceki sozluk kullaniliyor ({path})");
            }
            else if (pipeline.LlmAvailable)
            {
                Console.WriteLine($"[schema] {dataset.Name}: sozluk uretiliyor (LLM)…");
                artifact = await pipeline.BuildDictionaryAsync(profile, ct);
                Directory.CreateDirectory(outDir);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(artifact), ct);
            }

            return _dictionaries[dataset.Name] = artifact;
        }

        /// <summary>Yutulan SQL hatalarinin ozeti: veri seti basina sayi ve tekillesmis ilk mesaj.</summary>
        private string SqlErrorSummary()
        {
            var parts = pipeline.SqlErrors
                .Where(p => p.Value.Count > 0)
                .Select(p => $"{p.Key}: {p.Value.Count} ({string.Join(" / ", p.Value.Distinct().Take(2))})")
                .ToList();
            return parts.Count == 0 ? "yok" : string.Join(" | ", parts);
        }

        private async Task FinishAsync(string suite, DateTimeOffset started, List<Metric> metrics, List<Case> cases,
            List<string> notes, CancellationToken ct)
        {
            var (commit, branch) = RunOutput.Git();
            var document = new ScenarioRunDocument
            {
                Kind = "semantic",
                Suite = suite,
                Label = label,
                StartedAt = started,
                FinishedAt = DateTimeOffset.UtcNow,
                GitCommit = commit,
                GitBranch = branch,
                Target = server,
                Environment = new Dictionary<string, string>
                {
                    ["datasets"] = string.Join(",", datasets.Select(d => d.Name)),
                    ["llm"] = pipeline.LlmAvailable
                        ? System.Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "tanimli"
                        : reuseDictionary ? "onceki sozluk" : "tanimsiz",
                    ["notes"] = string.Join(" ", notes),
                    ["sqlErrors"] = SqlErrorSummary()
                },
                Metrics = metrics.Where(m => double.IsFinite(m.Value)).ToList(),
                Cases = cases
            };

            var path = await RunOutput.WriteAsync(document,
                Path.Combine(outDir, $"{suite}-{started.UtcDateTime:yyyyMMdd-HHmmss}.json"), ct);
            Console.WriteLine($"[sonuc] {path}");
            foreach (var metric in document.Metrics.Where(m => !m.Name.Contains('@')))
                Console.WriteLine($"  {metric.Name,-34} {(metric.Unit == "ratio" ? $"%{metric.Value * 100:0.#}" : $"{metric.Value:0.##}")}");

            if (publish is not null)
            {
                try { await RunOutput.PublishAsync(document, publish, publishKey, ct); }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
                {
                    Console.Error.WriteLine($"[dashboard] Gonderilemedi: {exception.Message}");
                }
            }
        }
    }
}

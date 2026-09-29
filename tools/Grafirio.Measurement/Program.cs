using System.Globalization;
using Grafirio.Measurement.Cli;
using Grafirio.Measurement.Evaluation;
using Grafirio.Measurement.Load;
using Grafirio.Measurement.Reporting;
using Grafirio.Measurement.Semantic;
using Grafirio.Measurement.Usage;

const string Help = """
    grafirio-measure — Grafirio olcum araci

    Komutlar:
      eval          Degerlendirme setini (dogruluk, tutarlilik, sure, maliyet) kosturur.
      load          Kademeli yuk testi (verim, gecikme, kirilma noktasi).
      usage         Gercek kullanim raporu (DataAnalysis veritabanindan, yalnizca toplu rakamlar).
      publish       Daha once yazilmis bir scenario-run/v1 JSON dosyasini dashboard'a gonderir.
      publish-bdn   BenchmarkDotNet *-report-full.json dosyalarini dashboard'a gonderir.
      semantic-live SQL Generation + Visualization Accuracy: semantik veri setinin sorularini canli
                    sisteme sorar, grafigi altin SQL sonucuyla karsilastirir.

    Ortak secenekler:
      --out <dosya>             Sonuc dosyasi (varsayilan: artifacts/olcum/<tur>-<set>-<zaman>.json)
      --publish <url>           Sonucu dashboard'a da gonder (orn. http://localhost:5080)   [MEASURE_DASHBOARD_URL]
      --publish-key <anahtar>   Dashboard Ingest:ApiKey tanimliysa                         [MEASURE_DASHBOARD_KEY]
      --label <metin>           Kosuya dashboard'da gorunecek ad

    eval:
      --suite <dosya>           Set dosyasi (orn. evals/ornek.json)
      --base-url <url>          Gateway adresi (varsayilan http://localhost:5000)          [GRAFIRIO_BASE_URL]
      --prefix <yol>            Veri analizi on eki (varsayilan data-analysis; servise dogrudan gidiliyorsa "")
      --token <jwt>             Keycloak erisim token'i                                   [GRAFIRIO_TOKEN]
      --connection <guid>       Kayitli baglanti (set dosyasindakini ezer)
      --repeat <n>              Her soru kac kez sorulsun (varsayilan 3; tutarlilik icin >= 2)
      --pass-threshold <0..1>   Vakanin basarili sayilmasi icin gereken dogruluk (varsayilan 1)
      --timeout <sure>          Soru basina en uzun bekleme (varsayilan 5m)
      --case <metin>            Yalnizca adi bunu iceren vakalar
      --price-input / --price-cached-input / --price-output <USD/1M token>
                                [LLM_PRICE_INPUT_PER_MILLION / ..._CACHED_INPUT_... / ..._OUTPUT_...]

    load:
      --scenario <ad>           health | history | question | custom
      --base-url, --prefix, --token, --connection   (eval ile ayni)
      --path "<METOT> <yol>"    custom senaryo icin, orn. "GET data-analysis/health"
      --stages 1,5,10,25,50     Eszamanli kullanici kademeleri
      --duration 30s            Kademe basina sure
      --warmup 5s               Olculmeyen isinma
      --slo-p95 2000            Kabul edilebilir p95 (ms)
      --max-error-rate 0.01     Kabul edilebilir hata orani
      --allow-llm               "question" senaryosu LLM'e gercek para harcatir; acikca izin verilmeli

    usage:
      --db <baglanti dizesi>    DataAnalysis Postgres baglantisi                           [GRAFIRIO_ANALYSIS_DB]
      --days <n>                Son n gun (varsayilan 30)
      --bucket day|week         Kirilim (varsayilan week)
      --price-* ...             (eval ile ayni)

    semantic-live:
      --dataset <klasor>        Veri seti (orn. evals/semantic/eticaret)
      --connection <guid>       Grafirio.daki kayitli baglanti — veri setinin kuruldugu veritabanina
      --gold-db <dize>          AYNI veritabanina ADO.NET baglanti dizesi (altin SQL icin)  [SEMANTIC_GOLD_DB]
      --base-url, --prefix, --token, --timeout   (eval ile ayni)

    publish <dosya.json> --publish <url>
    publish-bdn [klasor] --publish <url>   (varsayilan klasor: BenchmarkDotNet.Artifacts/results)
    """;

var args2 = args;
if (args2.Length == 0 || args2[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Help);
    return 0;
}

var command = args2[0];
var cli = new CommandLine(args2.Skip(1));
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

try
{
    switch (command)
    {
        case "eval":
            return await RunEvalAsync(cli, cancel.Token);
        case "load":
            return await RunLoadAsync(cli, cancel.Token);
        case "usage":
            return await RunUsageAsync(cli, cancel.Token);
        case "publish":
            return await PublishFileAsync(cli, cancel.Token);
        case "publish-bdn":
            return await PublishBdnAsync(cli, cancel.Token);
        case "semantic-live":
            return await RunSemanticLiveAsync(cli, cancel.Token);
        default:
            Console.Error.WriteLine($"Bilinmeyen komut: {command}\n");
            Console.WriteLine(Help);
            return 2;
    }
}
catch (UsageException exception)
{
    Console.Error.WriteLine($"Hata: {exception.Message}\n\n'grafirio-measure --help' ile secenekleri gorun.");
    return 2;
}
catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException
                                      or FileNotFoundException or HttpRequestException)
{
    Console.Error.WriteLine($"Hata: {exception.Message}");
    return 1;
}

static async Task<int> RunEvalAsync(CommandLine cli, CancellationToken ct)
{
    var suite = EvalSuite.Load(cli.Require("suite"));
    var connection = cli.Get("connection") is { } text ? Guid.Parse(text)
        : suite.ConnectionId ?? throw new UsageException("--connection gerekli (set dosyasinda connectionId yok).");
    var baseUrl = cli.Get("base-url", "GRAFIRIO_BASE_URL") ?? "http://localhost:5000";
    var timeout = cli.Duration("timeout", TimeSpan.FromMinutes(5));

    var options = new EvalOptions
    {
        Suite = suite,
        ConnectionId = connection,
        BaseUrl = baseUrl,
        Repeat = Math.Max(1, cli.Int("repeat", suite.Repeat ?? 3)),
        PassThreshold = cli.Double("pass-threshold", 1.0),
        QuestionTimeout = timeout,
        Pricing = Pricing(cli),
        Label = cli.Get("label"),
        CaseFilter = cli.Get("case")
    };

    var api = GrafirioApi.Create(baseUrl, cli.Get("prefix") ?? "data-analysis", cli.Require("token", "GRAFIRIO_TOKEN"),
        timeout + TimeSpan.FromSeconds(30));
    var output = ReadOutputOptions(cli);
    RejectUnknown(cli);

    Console.WriteLine($"[eval] {suite.Suite}: {options.Suite.Cases.Count} vaka x {options.Repeat} tekrar → {baseUrl}");
    var document = await new EvalRunner(api, options).RunAsync(ct);
    await FinishAsync(document, output, ct);

    PrintSummary(document);
    return document.Cases.All(c => c.Success) ? 0 : 3;
}

static async Task<int> RunSemanticLiveAsync(CommandLine cli, CancellationToken ct)
{
    var dataset = SemanticDataset.Load(cli.Require("dataset"));
    var baseUrl = cli.Get("base-url", "GRAFIRIO_BASE_URL") ?? "http://localhost:5000";
    var timeout = cli.Duration("timeout", TimeSpan.FromMinutes(5));
    var options = new SemanticLiveOptions
    {
        Dataset = dataset,
        ConnectionId = Guid.Parse(cli.Require("connection")),
        BaseUrl = baseUrl,
        GoldConnectionString = cli.Require("gold-db", "SEMANTIC_GOLD_DB"),
        QuestionTimeout = timeout,
        Label = cli.Get("label")
    };
    var api = GrafirioApi.Create(baseUrl, cli.Get("prefix") ?? "data-analysis", cli.Require("token", "GRAFIRIO_TOKEN"),
        timeout + TimeSpan.FromSeconds(30));
    var output = ReadOutputOptions(cli);
    RejectUnknown(cli);

    var (sql, chart) = await new SemanticLiveRunner(api, options).RunAsync(ct);
    foreach (var document in new[] { sql, chart })
    {
        // Iki belge ayni --out'a yazilmasin: set adi dosya adina giriyor.
        var path = output.Out is null ? null
            : Path.Combine(Path.GetDirectoryName(output.Out) ?? ".", $"{document.Suite}-{Path.GetFileName(output.Out)}");
        await FinishAsync(document, (path, output.PublishUrl, output.PublishKey), ct);
        PrintSummary(document);
    }

    return 0;
}

static async Task<int> RunLoadAsync(CommandLine cli, CancellationToken ct)
{
    var scenario = cli.Require("scenario");
    var baseUrl = cli.Get("base-url", "GRAFIRIO_BASE_URL") ?? "http://localhost:5000";
    var prefix = (cli.Get("prefix") ?? "data-analysis").Trim('/');
    string P(string path) => prefix.Length == 0 ? path : $"{prefix}/{path}";

    LoadRequest request = scenario switch
    {
        "health" => new LoadRequest(HttpMethod.Get, "health"),
        "history" => new LoadRequest(HttpMethod.Get, P($"api/agent/queries/{cli.Require("connection")}")),
        "question" when !cli.Flag("allow-llm") =>
            throw new UsageException("'question' senaryosu her istekte LLM'e para harcatir; --allow-llm ile acikca izin verin."),
        "question" => new LoadRequest(HttpMethod.Post, P("api/agent/query"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                connectionId = Guid.Parse(cli.Require("connection")),
                question = cli.Require("question")
            })),
        "custom" => ParseCustom(cli.Require("path")),
        _ => throw new UsageException("--scenario health | history | question | custom olmali.")
    };

    var options = new LoadOptions
    {
        Scenario = scenario,
        BaseUrl = baseUrl,
        Request = request,
        Token = cli.Get("token", "GRAFIRIO_TOKEN"),
        Stages = (cli.Get("stages") ?? "1,5,10,25,50").Split(',').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture))
            .Where(s => s > 0).ToList(),
        StageDuration = cli.Duration("duration", TimeSpan.FromSeconds(30)),
        Warmup = cli.Duration("warmup", TimeSpan.FromSeconds(5)),
        P95SloMs = cli.Double("slo-p95", 2000),
        MaxErrorRate = cli.Double("max-error-rate", 0.01),
        Label = cli.Get("label")
    };

    if (scenario is "history" or "question" && string.IsNullOrEmpty(options.Token))
        throw new UsageException("Bu senaryo kimlik istiyor: --token ya da GRAFIRIO_TOKEN.");

    var output = ReadOutputOptions(cli);
    RejectUnknown(cli);

    var document = await new LoadRunner(options).RunAsync(ct);
    await FinishAsync(document, output, ct);
    PrintSummary(document);
    return 0;

    static LoadRequest ParseCustom(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            ? new LoadRequest(new HttpMethod(parts[0].ToUpperInvariant()), parts[1].TrimStart('/'))
            : new LoadRequest(HttpMethod.Get, parts[0].TrimStart('/'));
    }
}

static async Task<int> RunUsageAsync(CommandLine cli, CancellationToken ct)
{
    var connectionString = cli.Require("db", "GRAFIRIO_ANALYSIS_DB");
    var days = cli.Int("days", 30);
    var bucket = (cli.Get("bucket") ?? "week").ToLowerInvariant() switch
    {
        "day" => UsageBucket.Day,
        "week" => UsageBucket.Week,
        _ => throw new UsageException("--bucket day | week olmali.")
    };
    var until = DateTime.UtcNow.Date.AddDays(1);
    var since = until.AddDays(-days);
    var pricing = Pricing(cli);
    var label = cli.Get("label");
    var output = ReadOutputOptions(cli);
    RejectUnknown(cli);

    var (questions, analyses) = await UsageReport.LoadAsync(connectionString, since, until, ct);
    Console.WriteLine($"[usage] {questions.Count} soru, {analyses.Count} analiz okundu ({since:yyyy-MM-dd} – {until:yyyy-MM-dd}).");

    // Baglanti dizesinin yalnizca sunucu kismi hedef olarak yaziliyor; parola
    // belgeye girmemeli.
    var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
    var document = UsageReport.Build(questions, analyses, since, until, bucket, pricing,
        $"postgres://{builder.Host}:{builder.Port}/{builder.Database}", label);

    await FinishAsync(document, output, ct);
    PrintSummary(document);
    return 0;
}

static async Task<int> PublishFileAsync(CommandLine cli, CancellationToken ct)
{
    var file = cli.Positionals.FirstOrDefault() ?? throw new UsageException("Gonderilecek dosyayi verin.");
    var url = cli.Require("publish", "MEASURE_DASHBOARD_URL");
    var key = cli.Get("publish-key", "MEASURE_DASHBOARD_KEY");
    RejectUnknown(cli);

    await RunOutput.PublishAsync(await File.ReadAllTextAsync(file, ct), url, key, ct);
    return 0;
}

static async Task<int> PublishBdnAsync(CommandLine cli, CancellationToken ct)
{
    var folder = cli.Positionals.FirstOrDefault() ?? Path.Combine("BenchmarkDotNet.Artifacts", "results");
    var url = cli.Require("publish", "MEASURE_DASHBOARD_URL");
    var key = cli.Get("publish-key", "MEASURE_DASHBOARD_KEY");
    var label = cli.Get("label");
    RejectUnknown(cli);

    var files = Directory.Exists(folder)
        ? Directory.GetFiles(folder, "*-report-full*.json")
        : throw new UsageException($"{folder} bulunamadi. Once benchmark'lari calistirin.");
    if (files.Length == 0) throw new UsageException($"{folder} icinde *-report-full*.json yok.");

    await BdnPublisher.PublishAsync(files, url, key, label, ct);
    return 0;
}

static LlmPricing? Pricing(CommandLine cli)
{
    var input = cli.OptionalDouble("price-input", "LLM_PRICE_INPUT_PER_MILLION");
    var output = cli.OptionalDouble("price-output", "LLM_PRICE_OUTPUT_PER_MILLION");
    if (input is null && output is null) return null;
    var cached = cli.OptionalDouble("price-cached-input", "LLM_PRICE_CACHED_INPUT_PER_MILLION") ?? input ?? 0;
    return new LlmPricing(input ?? 0, cached, output ?? 0);
}

static (string? Out, string? PublishUrl, string? PublishKey) ReadOutputOptions(CommandLine cli) =>
    (cli.Get("out"), cli.Get("publish", "MEASURE_DASHBOARD_URL"), cli.Get("publish-key", "MEASURE_DASHBOARD_KEY"));

static async Task FinishAsync(ScenarioRunDocument document,
    (string? Out, string? PublishUrl, string? PublishKey) output, CancellationToken ct)
{
    var path = await RunOutput.WriteAsync(document, output.Out, ct);
    Console.WriteLine($"[sonuc] {path}");

    if (output.PublishUrl is not null)
    {
        try
        {
            await RunOutput.PublishAsync(document, output.PublishUrl, output.PublishKey, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            // Olcum kaybolmuyor: dosya yazildi, sonradan 'publish' ile gonderilebilir.
            Console.Error.WriteLine($"[dashboard] Gonderilemedi: {exception.Message}");
            Console.Error.WriteLine($"[dashboard] Sonra gondermek icin: grafirio-measure publish {path} --publish {output.PublishUrl}");
        }
    }
}

static void PrintSummary(ScenarioRunDocument document)
{
    Console.WriteLine();
    Console.WriteLine($"{document.Kind} · {document.Suite} · {document.Cases.Count(c => c.Success)}/{document.Cases.Count} basarili");
    foreach (var metric in document.Metrics)
        Console.WriteLine($"  {metric.Name,-28} {Format(metric)}");

    static string Format(Metric metric) => metric.Unit switch
    {
        "ratio" => $"%{metric.Value * 100:0.##}",
        "USD" => $"${metric.Value:0.####}",
        _ => $"{metric.Value:0.##} {metric.Unit}"
    };
}

static void RejectUnknown(CommandLine cli)
{
    var unknown = cli.Unknown.ToList();
    if (unknown.Count > 0)
        throw new UsageException("Bilinmeyen secenek: " + string.Join(", ", unknown.Select(u => "--" + u)));
}

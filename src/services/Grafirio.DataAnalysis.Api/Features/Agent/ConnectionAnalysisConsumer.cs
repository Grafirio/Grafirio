using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.DataAnalysis.Api.Services;
using MassTransit;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

/// <summary>
/// "Analiz Et" isteginin kuyruga dusen govdesi.
///
/// Sifre burada tasinmiyor: mesaj RabbitMQ'da bekliyor ve kuyruk icerigi
/// diskte durur. Tuketici baglantiyi kendisi okuyup sifreyi cozuyor.
/// </summary>
public record AnalyzeConnectionRequested
{
    public required Guid ConfigId { get; init; }
    public required Guid ConnectionId { get; init; }
    public required string CompanyId { get; init; }
    public required bool SamplingConsentGiven { get; init; }
}

/// <summary>
/// Semayi profilleyip semantik sozlugu ureten arka plan isi.
///
/// Onceden bu is ucun icinde <c>Task.Run</c> ile calisiyordu. Iki sorunu
/// vardi: is kuyrukta durmadigi icin container yeniden baslarsa analiz
/// sessizce kayboluyor ve kayit sonsuza kadar "analyzing" kaliyordu; ayrica
/// basarisiz bir LLM cagrisi icin yeniden deneme yoktu. Kuyruga alinca is
/// dayanikli hale geliyor, MassTransit'in yeniden deneme politikasi da
/// gecici Azure hatalarini (429, 503) kendi basina karsiliyor.
/// </summary>
public class ConnectionAnalysisConsumer(
    DataAnalysisDbContext db,
    LlmAnalysisService llm,
    SchemaProfiler profiler,
    IDataSourceFactory dataSources,
    LearnedFactStore facts,
    ILogger<ConnectionAnalysisConsumer> logger)
    : IConsumer<AnalyzeConnectionRequested>
{
    public async Task Consume(ConsumeContext<AnalyzeConnectionRequested> context)
    {
        var message = context.Message;
        var ct = context.CancellationToken;

        var config = await db.AnalysisConfigs.FindAsync([message.ConfigId], ct);
        if (config is null)
        {
            // Kayit silinmisse yapacak bir sey yok; mesaji tekrar denemek de
            // ayni sonucu verir, o yuzden sessizce tuketiliyor.
            logger.LogWarning("Analiz kaydı bulunamadı, mesaj atlanıyor: {ConfigId}", message.ConfigId);
            return;
        }

        var connection = await db.SavedConnections.FindAsync([message.ConnectionId], ct);
        if (connection is null || connection.CompanyId != message.CompanyId)
        {
            await Fail(config, "Bağlantı bulunamadı.", ct);
            return;
        }

        var selectedTables = JsonSerializer.Deserialize<List<string>>(config.TablesJson) ?? [];
        if (selectedTables.Count == 0)
        {
            await Fail(config, "Tablo seçimi boş.", ct);
            return;
        }

        try
        {
            await using var session = await dataSources.OpenAsync(connection, ct);

            // Kullanicinin daha once ogrettikleri. Sozluk her analizde
            // sifirdan uretiliyor; bunlar duruyor ve uzerine isleniyor.
            // Reddedilenler de geliyor — onlar sozluge islenmez, yalnizca
            // "bir daha sorma" kaydidir.
            var learned = await facts.GetAllAsync(message.ConnectionId, message.CompanyId, ct);
            var accepted = learned.Where(f => f.Accepted).ToList();

            if (learned.Count > 0)
                logger.LogInformation(
                    "Öğrenilmiş bilgi okundu: {Accepted} onaylı, {Rejected} reddedilmiş.",
                    accepted.Count, learned.Count - accepted.Count);

            var declaredLinks = LinksOf(accepted);
            var rejectedLinks = LinksOf(learned.Where(f => !f.Accepted));

            // Kurulamayan beyanlar buraya düşüyor ve kayıtlarının üstüne
            // yazılıyor. Sessizce düşürmek, kullanıcıyı kurduğu bağlantının
            // hâlâ çalıştığına inandırmak olurdu.
            var declaredProblems = new List<RelationshipDiscovery.DeclaredProblem>();

            var profile = await profiler.ProfileAsync(
                session, connection.Database, selectedTables, message.SamplingConsentGiven,
                declaredLinks, rejectedLinks, declaredProblems, ct);

            await RecordDeclaredProblemsAsync(message, learned, declaredProblems, ct);

            // Sozluk tek cagriyla uretilemiyor: cikti kolon sayisiyla dogru
            // orantili buyudugu icin birkac yuz kolonda cevap token butcesine
            // sigmiyor ve model bos donuyor. Tablolar kolon butcesine gore
            // gruplanip ayri ayri soruluyor, sonuclar birlestiriliyor.
            // Sema tek parcaya sigiyorsa hicbir sey degismiyor.
            // Olcum, parcanin GERCEKTEN gonderilecek JSON'unu uretip
            // uzunluguna bakiyor. Uzunlugu string uretmeden hesaplayan bir yol
            // yazilabilirdi; olculdu ve degmiyor: 2020 kolonluk bir semada
            // bolme boyunca 42 olcum yapiliyor, toplam ~660 bin karakter
            // (~1,3 MB gecici, gen-0). Hemen ardindan gelen 21 HTTP cagrisinin
            // istek govdeleri bunun uc kati. Tahmin eden bir yardimciysa
            // gercek boyuttan sapabilir ve butun mesele zaten dogru olcmekti.
            var chunks = DictionaryChunks.Split(
                profile, chunk => PromptProfile.Serialize(chunk, JsonOptions).Length);
            // Modele giden profil, sakladigimiz profilin aynisi degil: karar
            // verirken kullanilmayan alanlar (ornekleme gerekcesi, her satirda
            // tekrar eden bayraklar) cikariliyor. Profil nesnesi olduğu gibi
            // duruyor — codeValues ve profileStats onu okuyor.
            var chunkProfiles = chunks
                .Select(c => PromptProfile.Serialize(c, JsonOptions))
                .ToList();
            var allTableNames = profile.Tables.Select(t => t.Qualified).ToList();

            if (chunks.Count > 1)
            {
                logger.LogInformation(
                    "Şema {Tables} tablo / {Columns} kolon: sözlük {Chunks} parçada üretilecek.",
                    profile.Tables.Count, profile.Tables.Sum(t => t.Columns.Count), chunks.Count);
            }

            // Ilerleme kayda yaziliyor ki ekranda gorunebilsin. Yirmi parcalik
            // bir semada analiz on bes dakika surebiliyor; o sureyi hicbir sey
            // yazmayan bir spinner karsisinda gecirmek, kullaniciya "sistem
            // kilitlendi" dedirtiyor. `SchemaSummary` bu durumda zaten mesaj
            // tasiyicisi olarak kullaniliyor (bkz. `Fail`); sonuc geldiginde
            // gercek ozetle degistiriliyor.
            Func<int, int, Task>? onProgress = chunks.Count > 1
                ? async (done, total) =>
                {
                    config.SchemaSummary = $"Sözlük üretiliyor: {done}/{total} parça tamamlandı.";
                    config.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            : null;

            if (onProgress is not null) await onProgress(0, chunks.Count);

            var result = await llm.BuildSchemaDictionaryAsync(
                chunkProfiles, allTableNames, onProgress, ct);

            if (!result.Success)
            {
                // Yapilandirma hatasi yeniden denemekle duzelmez; digerleri
                // gecici olabilir, MassTransit'e birakiliyor.
                if (result.IsConfigurationError)
                {
                    await Fail(config, result.Error ?? "LLM yapılandırılmamış.", ct);
                    return;
                }

                throw new InvalidOperationException(result.Error ?? "Sözlük üretilemedi.");
            }

            var dictionary = AttachProfileFacts(result.Json, profile, accepted);

            config.ConfigJson = dictionary;
            config.SchemaSummary = result.Explanation;
            config.Status = AgentAnalyzeEndpoints.ExtractQuestions(dictionary).Count > 0
                ? AgentAnalyzeEndpoints.AnalysisStatus.AwaitingAnswers
                : AgentAnalyzeEndpoints.AnalysisStatus.Ready;
            config.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Analiz tamamlandı. Connection: {ConnectionId}, durum: {Status}",
                message.ConnectionId, config.Status);
        }
        catch (Exception ex) when (context.GetRetryAttempt() >= MaxRetries)
        {
            // Son deneme de tutmadi: kullanici sonsuza kadar donen bir spinner
            // yerine sebebi gorsun.
            logger.LogError(ex, "Analiz başarısız. Connection: {ConnectionId}", message.ConnectionId);
            await Fail(config, ex.Message, ct);
        }
    }

    /// <summary>
    /// Kurulamayan beyanlari kendi kayitlarinin ustune yazar; kurulabilenlerin
    /// eski uyarisini temizler.
    ///
    /// Ikinci yari birincisi kadar onemli: sema duzeldiginde ekranda asili
    /// kalan bir uyari, kullaniciyi olmayan bir sorunu kovalamaya gonderir.
    /// </summary>
    private async Task RecordDeclaredProblemsAsync(
        AnalyzeConnectionRequested message,
        IReadOnlyList<LearnedFact> learned,
        IReadOnlyList<RelationshipDiscovery.DeclaredProblem> problems,
        CancellationToken ct)
    {
        var relationships = learned.Where(f => f.Kind == LearnedFact.Relationship && f.Accepted).ToList();
        if (relationships.Count == 0) return;

        // Sorunlar cozulmus adlarla geliyor (semadaki yazim), kayitlar ise
        // kullanicinin yazdigi adlarla. Ikisini ayni anahtar uzerinden
        // eslestiriyoruz.
        var failed = problems.ToDictionary(
            p => LearnedFact.RelationshipKey(
                p.Link.FromTable, p.Link.FromColumn, p.Link.ToTable, p.Link.ToColumn),
            p => p.Reason,
            StringComparer.Ordinal);

        foreach (var fact in relationships)
        {
            var reason = failed.GetValueOrDefault(fact.Key);

            // Durumu degismeyen kayda dokunulmuyor: her analizde butun
            // kayitlari yeniden yazmak gereksiz yazma trafigi.
            if (reason is null && fact.Problem is null) continue;

            await facts.SetStatusAsync(message.ConnectionId, message.CompanyId, fact.Key, reason, ct);
        }

        if (failed.Count > 0)
            logger.LogWarning(
                "{Count} beyan edilen bağlantı bu analizde kurulamadı; kayıtlarına işlendi.",
                failed.Count);
    }

    /// <summary>
    /// Iliski kayitlarini kesif hattinin anladigi bicime cevirir. Iliski
    /// disindaki turler (es anlamli, tanim…) burada elenir.
    /// </summary>
    private static List<RelationshipDiscovery.DeclaredLink> LinksOf(
        IEnumerable<LearnedFact> facts) => facts
            .Where(f => f.Kind == LearnedFact.Relationship)
            .Select(f => new RelationshipDiscovery.DeclaredLink(
                f.FromTable ?? "", f.FromColumn ?? "", f.ToTable ?? "", f.ToColumn ?? ""))
            .ToList();

    /// <summary>
    /// MassTransit yapilandirmasindaki yeniden deneme sayisiyla ayni olmali;
    /// son denemede hatayi yutup kaydi "failed" isaretlemek icin gerekiyor.
    /// </summary>
    public const int MaxRetries = 2;

    /// <summary>
    /// Bu sayidan fazla ayrik degeri olan kolon "kod kolonu" sayilmaz.
    /// Ayirt edici kolonlarda tipik olarak bir avuc deger bulunur; esigi
    /// yukseltmek sozlugu sisirir ve modele ise yaramayan veri gosterir.
    /// </summary>
    private const int CodeValueThreshold = 50;

    private async Task Fail(Data.Entities.AnalysisConfig config, string reason, CancellationToken ct)
    {
        config.Status = AgentAnalyzeEndpoints.AnalysisStatus.Failed;
        config.SchemaSummary = reason;
        config.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Modelin uretmedigi, ama olculmus olan gercekleri sozluge ekler.
    ///
    /// Iliskiler bilerek burada tasiniyor: bunlar LLM'in yorumu degil,
    /// veritabanindan okunmus ve deger ortusmesiyle dogrulanmis olculerdir.
    /// Onceden profil yalnizca prompt icinde gorunuyor, sonra atiliyordu —
    /// yani hangi baglantilarin bulundugu hicbir yerde kalmiyordu. Sorgu
    /// aninda yol takibi yapabilmenin on kosulu bunlarin kalici olmasi.
    /// </summary>
    /// <param name="learned">
    /// Kullanicinin ONAYLADIGI bilgiler. Reddedilenler buraya hic gelmez:
    /// onlar sozlukte degil, "bir daha sorma" kaydinda yasar.
    ///
    /// Iliski beyanlari burada islenmiyor — onlar profil cikarilirken olcum
    /// kapisindan gecip <c>profile.Relationships</c> icine girdi zaten.
    /// </param>
    private static string AttachProfileFacts(
        string dictionaryJson, DatabaseProfile profile, IReadOnlyList<LearnedFact> learned)
    {
        try
        {
            if (JsonNode.Parse(dictionaryJson) is not JsonObject root) return dictionaryJson;

            root["profileStats"] = new JsonObject
            {
                ["tableCount"] = profile.Tables.Count,
                ["columnCount"] = profile.Tables.Sum(t => t.Columns.Count),
                ["sampledColumnCount"] = profile.Tables.Sum(t => t.Columns.Count(c => c.SampleValues.Count > 0)),
                ["relationshipCount"] = profile.Relationships.Count,
                ["inferredRelationshipCount"] = profile.Relationships.Count(r => r.Source == "inferred")
            };

            root["relationships"] = JsonSerializer.SerializeToNode(profile.Relationships, JsonOptions);

            // Az sayida ayrik degeri olan kolonlarin degerleri. Bunlar da
            // olculmus gercek, modelin yorumu degil.
            //
            // Neden gerekli: bir parametre tablosu cogu zaman tek basina birden
            // fazla seyi tutar — para birimleri, odeme tipleri, durum kodlari
            // hepsi ayni tabloda, bir `Tip` kolonuyla ayrilmis. Boyle bir
            // tabloya iki kez baglanmak icin ON'a "AND Tip = 'CUR'" girmesi
            // gerekiyor ve modelin 'CUR' diye bir kod oldugunu bilmesinin baska
            // yolu yok. Degerler yalnizca gizlilik politikasinin ornek
            // toplamaya izin verdigi kolonlardan geliyor: `SampleValues`
            // izin verilmeyen kolonlarda zaten bos.
            var codeValues = new JsonArray();
            foreach (var table in profile.Tables)
            {
                foreach (var column in table.Columns)
                {
                    if (column.SampleValues.Count == 0) continue;
                    if (column.DistinctCount is null or > CodeValueThreshold) continue;

                    codeValues.Add(new JsonObject
                    {
                        ["table"] = $"{table.Schema}.{table.TableName}",
                        ["column"] = column.ColumnName,
                        ["values"] = new JsonArray(
                            column.SampleValues.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray())
                    });
                }
            }

            root["codeValues"] = codeValues;

            ApplyLearnedFacts(root, learned);

            return root.ToJsonString(JsonOptions);
        }
        catch (JsonException)
        {
            return dictionaryJson;
        }
    }

    /// <summary>
    /// Kullanicinin ogrettiklerini sozluge isler. Model her analizde sozlugu
    /// sifirdan uretiyor; bu adim olmadan ogrenilen her sey her "Analiz Et"te
    /// kayboluyordu.
    ///
    /// Uc tur isleniyor, ucu de sozlugun ZATEN OKUNAN alanlarina yaziliyor —
    /// ceviri prompt'u degismiyor, motor tarafinda hicbir sey degismiyor.
    /// </summary>
    public static void ApplyLearnedFacts(JsonObject root, IReadOnlyList<LearnedFact> learned)
    {
        if (learned.Count == 0) return;

        var tables = Ensure(root, "tables");
        var columns = Ensure(root, "columns");

        foreach (var fact in learned)
        {
            switch (fact.Kind)
            {
                // "gelir dedigimde EarningAmount'u kastediyorum" — kullanicinin
                // kelimesi `synonyms`'e giriyor, ceviri zaten oradan esliyor.
                case LearnedFact.Synonym when !string.IsNullOrWhiteSpace(fact.Means):
                    var target = string.IsNullOrWhiteSpace(fact.Column)
                        ? FindOrAddTable(tables, fact.Table)
                        : FindOrAddColumn(columns, fact.Table, fact.Column);
                    AddSynonym(target, fact.Means!);
                    target["source"] = "user";
                    break;

                // "Analiz Et" sorularinin cevaplari. Tanim, sozlukteki
                // tanimin uzerine yaziliyor: modelin tahmini degil,
                // veritabanini bilen kisinin cevabi.
                case LearnedFact.Meaning when !string.IsNullOrWhiteSpace(fact.Means):
                    if (string.IsNullOrWhiteSpace(fact.Column))
                    {
                        var table = FindOrAddTable(tables, fact.Table);
                        table["purpose"] = fact.Means;
                        table["confidence"] = "high";
                        table["source"] = "user";
                    }
                    else
                    {
                        var column = FindOrAddColumn(columns, fact.Table, fact.Column);
                        column["meaning"] = fact.Means;
                        column["confidence"] = "high";
                        column["source"] = "user";
                    }
                    break;

                // "ROD karayolu demek" — olculmus deger listesinin yanina
                // anlami yaziliyor. Filtre yazarken model artik kullanicinin
                // "kara" demesiyle 'ROD' kodunu birlestirebiliyor.
                case LearnedFact.CodeMeaning
                    when !string.IsNullOrWhiteSpace(fact.Value) && !string.IsNullOrWhiteSpace(fact.Means):
                    AttachCodeMeaning(root, fact);
                    break;

                // "Bu tablonun okunabilir adi su kolonda" — hedef tablosu bu
                // olan her kenarin etiketi degistiriliyor. J4 kurali zaten
                // `labelColumn`'u okuyor.
                case LearnedFact.Label when !string.IsNullOrWhiteSpace(fact.Column):
                    OverrideLabelColumn(root, fact);
                    break;
            }
        }
    }

    private static JsonArray Ensure(JsonObject root, string name)
    {
        if (root[name] is JsonArray existing) return existing;
        var created = new JsonArray();
        root[name] = created;
        return created;
    }

    private static bool Same(JsonNode? node, string? value) =>
        string.Equals(node?.GetValue<string>(), value, StringComparison.OrdinalIgnoreCase);

    private static JsonObject FindOrAddTable(JsonArray tables, string? name)
    {
        var found = tables.OfType<JsonObject>().FirstOrDefault(t => Same(t["name"], name));
        if (found is not null) return found;

        var created = new JsonObject { ["name"] = name, ["confidence"] = "high" };
        tables.Add(created);
        return created;
    }

    private static JsonObject FindOrAddColumn(JsonArray columns, string? table, string? column)
    {
        var found = columns.OfType<JsonObject>()
            .FirstOrDefault(c => Same(c["table"], table) && Same(c["column"], column));
        if (found is not null) return found;

        var created = new JsonObject
        {
            ["table"] = table,
            ["column"] = column,
            ["role"] = "other",
            ["confidence"] = "high"
        };
        columns.Add(created);
        return created;
    }

    private static void AddSynonym(JsonObject entry, string synonym)
    {
        if (entry["synonyms"] is not JsonArray list)
        {
            list = [];
            entry["synonyms"] = list;
        }

        // Ayni kelime iki kez ogretilirse listede iki kez durmasin.
        if (list.Any(n => Same(n, synonym))) return;
        list.Add(synonym);
    }

    private static void AttachCodeMeaning(JsonObject root, LearnedFact fact)
    {
        var entry = (root["codeValues"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(c => Same(c["table"], fact.Table) && Same(c["column"], fact.Column));

        // Kolon olculmus deger listesinde yoksa anlam yazacak yer de yok.
        // Bu sessiz bir kayip degil: boyle bir kolon zaten filtrelenemiyor.
        if (entry is null) return;

        if (entry["meanings"] is not JsonObject meanings)
        {
            meanings = [];
            entry["meanings"] = meanings;
        }

        meanings[fact.Value!] = fact.Means;
    }

    private static void OverrideLabelColumn(JsonObject root, LearnedFact fact)
    {
        if (root["relationships"] is not JsonArray edges) return;

        foreach (var edge in edges.OfType<JsonObject>())
            if (Same(edge["toTable"], fact.Table))
                edge["labelColumn"] = fact.Column;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

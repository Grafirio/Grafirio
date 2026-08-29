using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
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

            var profile = await profiler.ProfileAsync(
                session, connection.Database, selectedTables, message.SamplingConsentGiven, ct);

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

            var dictionary = AttachProfileFacts(result.Json, profile);

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
    private static string AttachProfileFacts(string dictionaryJson, DatabaseProfile profile)
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

            return root.ToJsonString(JsonOptions);
        }
        catch (JsonException)
        {
            return dictionaryJson;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

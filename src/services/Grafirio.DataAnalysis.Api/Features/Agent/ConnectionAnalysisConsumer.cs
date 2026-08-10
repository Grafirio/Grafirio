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

            var profileJson = JsonSerializer.Serialize(profile, JsonOptions);
            var result = await llm.BuildSchemaDictionaryAsync(profileJson, ct);

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

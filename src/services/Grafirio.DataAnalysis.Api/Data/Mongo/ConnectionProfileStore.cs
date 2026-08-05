using MongoDB.Bson;
using MongoDB.Driver;

namespace Grafirio.DataAnalysis.Api.Data.Mongo;

/// <summary>
/// Baglantiya ait tablo secimi ve sema profili icin kalici depo.
///
/// Neden Postgres degil de Mongo:
///   - Postgres semasi <c>EnsureCreated()</c> ile kuruluyor ve migration yok;
///     mevcut veritabanina yeni kolon eklenmiyor.
///   - Profil dogasi geregi semasiz ve ic ice bir dokuman (tablolar, kolonlar,
///     ornek degerler, semantik sozluk). Iliskisel kolona sigdirmak zorlama olurdu.
///
/// Sema profili <see cref="EncryptionHelper"/> ile sifrelenerek yaziliyor:
/// icinde musteri veritabanindan alinmis ornek degerler bulunuyor.
/// </summary>
public class ConnectionProfileStore
{
    private readonly IMongoCollection<BsonDocument> _collection;
    private readonly ILogger<ConnectionProfileStore> _logger;

    public const string CollectionName = "ConnectionProfiles";

    public ConnectionProfileStore(IMongoDatabase database, ILogger<ConnectionProfileStore> logger)
    {
        _collection = database.GetCollection<BsonDocument>(CollectionName);
        _logger = logger;
    }

    private static FilterDefinition<BsonDocument> ById(Guid connectionId, string companyId) =>
        Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", connectionId.ToString()),
            // Sirket suzgeci burada da var: baska bir sirketin baglanti kimligini
            // bilen biri profili okuyamasin.
            Builders<BsonDocument>.Filter.Eq("companyId", companyId));

    /// <summary>Kullanicinin sectigi tablolari kaydeder.</summary>
    public async Task SaveSelectedTablesAsync(
        Guid connectionId, string companyId, IReadOnlyList<string> tables, CancellationToken ct = default)
    {
        var update = Builders<BsonDocument>.Update
            .Set("companyId", companyId)
            .Set("selectedTables", new BsonArray(tables))
            .Set("selectedTablesUpdatedAt", DateTime.UtcNow)
            // Tablo secimi degistiginde eski profil gecersizdir; durum sifirlanir
            // ki kullanici yeniden on analiz calistirsin.
            .Unset("profile")
            .Set("status", ProfileStatus.Pending);

        await _collection.UpdateOneAsync(
            ById(connectionId, companyId), update, new UpdateOptions { IsUpsert = true }, ct);

        _logger.LogInformation(
            "Tablo secimi kaydedildi. Connection: {ConnectionId}, tablo sayisi: {Count}",
            connectionId, tables.Count);
    }

    public async Task<List<string>> GetSelectedTablesAsync(
        Guid connectionId, string companyId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(ById(connectionId, companyId)).FirstOrDefaultAsync(ct);
        if (doc is null || !doc.TryGetValue("selectedTables", out var value) || !value.IsBsonArray)
            return [];

        return value.AsBsonArray.Select(v => v.AsString).ToList();
    }

    /// <summary>Sema profilini sifreleyerek yazar.</summary>
    public async Task SaveProfileAsync(
        Guid connectionId, string companyId, string profileJson, string status, CancellationToken ct = default)
    {
        var update = Builders<BsonDocument>.Update
            .Set("companyId", companyId)
            .Set("profile", EncryptionHelper.Encrypt(profileJson))
            .Set("status", status)
            .Set("profileUpdatedAt", DateTime.UtcNow);

        await _collection.UpdateOneAsync(
            ById(connectionId, companyId), update, new UpdateOptions { IsUpsert = true }, ct);
    }

    public async Task<(string? ProfileJson, string Status)> GetProfileAsync(
        Guid connectionId, string companyId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(ById(connectionId, companyId)).FirstOrDefaultAsync(ct);
        if (doc is null) return (null, ProfileStatus.Pending);

        var status = doc.TryGetValue("status", out var s) && s.IsString ? s.AsString : ProfileStatus.Pending;

        if (!doc.TryGetValue("profile", out var p) || !p.IsString || string.IsNullOrEmpty(p.AsString))
            return (null, status);

        return (EncryptionHelper.Decrypt(p.AsString), status);
    }

    public async Task SetStatusAsync(
        Guid connectionId, string companyId, string status, string? note = null, CancellationToken ct = default)
    {
        var update = Builders<BsonDocument>.Update
            .Set("companyId", companyId)
            .Set("status", status)
            .Set("statusUpdatedAt", DateTime.UtcNow);

        if (note is not null) update = update.Set("statusNote", note);

        await _collection.UpdateOneAsync(
            ById(connectionId, companyId), update, new UpdateOptions { IsUpsert = true }, ct);
    }
}

/// <summary>On analizin bekletici kapisindaki durumlar.</summary>
public static class ProfileStatus
{
    /// <summary>Tablo secilmis ama on analiz hic calistirilmamis.</summary>
    public const string Pending = "pending";

    /// <summary>Sema ve ornek degerler toplaniyor.</summary>
    public const string Profiling = "profiling";

    /// <summary>LLM emin olamadigi seyleri sordu, kullanici yaniti bekleniyor.</summary>
    public const string AwaitingAnswers = "awaiting_answers";

    /// <summary>Kullanilabilir: dashboard'a dusebilir, grafik uretebilir.</summary>
    public const string Ready = "ready";

    public const string Failed = "failed";
}

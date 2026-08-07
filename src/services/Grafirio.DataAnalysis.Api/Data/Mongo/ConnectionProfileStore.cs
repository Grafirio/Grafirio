using MongoDB.Bson;
using MongoDB.Driver;

namespace Grafirio.DataAnalysis.Api.Data.Mongo;

/// <summary>
/// Baglantiya ait tablo secimi icin kalici depo.
///
/// Neden Postgres degil de Mongo: Postgres semasi <c>EnsureCreated()</c> ile
/// kuruluyor ve migration yok, yani mevcut veritabanina yeni kolon eklenmiyor.
/// Secim de degisken uzunlukta bir liste.
///
/// Bu depo bir zamanlar sema profilini ve semantik sozlugu de tutuyordu.
/// Analizin durumu boylece iki yerde birden yasiyordu — burada ve
/// <c>AnalysisConfig.Status</c>'te — ve ikisi birbirinden habersiz
/// ilerleyebiliyordu. Analiz ciktisi artik tek yerde, Postgres'te; burasi
/// yalnizca "hangi tablolar secildi" sorusunu cevapliyor.
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
            // bilen biri secimi okuyamasin.
            Builders<BsonDocument>.Filter.Eq("companyId", companyId));

    /// <summary>Kullanicinin sectigi tablolari kaydeder.</summary>
    public async Task SaveSelectedTablesAsync(
        Guid connectionId, string companyId, IReadOnlyList<string> tables, CancellationToken ct = default)
    {
        var update = Builders<BsonDocument>.Update
            .Set("companyId", companyId)
            .Set("selectedTables", new BsonArray(tables))
            .Set("selectedTablesUpdatedAt", DateTime.UtcNow)
            // Eski surumlerden kalan profil alanlari temizleniyor: icerikleri
            // musteri veritabanindan alinmis ornek degerler ve artik
            // okunmuyorlar.
            .Unset("profile")
            .Unset("status");

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
}

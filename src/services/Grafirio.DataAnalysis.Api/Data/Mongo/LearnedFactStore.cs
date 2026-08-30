using MongoDB.Bson;
using MongoDB.Driver;

namespace Grafirio.DataAnalysis.Api.Data.Mongo;

/// <summary>
/// Kullanicinin sisteme ogrettiklerinin kalici deposu.
///
/// Neden ayri bir koleksiyon: kayitlarin tek tek yazilmasi, okunmasi ve
/// SILINMESI gerekiyor. Bunlari <see cref="ConnectionProfileStore"/>'daki
/// belgenin icine dizi olarak koymak, her yazmada diziyi anahtara gore
/// once cikarip sonra eklemeyi gerektirirdi — iki islem, arasinda yaris.
/// Her gercek kendi belgesi olunca upsert de silme de tek ve atomik.
///
/// Belge kimligi <c>{connectionId}:{key}</c>. Ayni bilgi ikinci kez
/// ogretilirse yeni satir acilmiyor, ustune yaziliyor: "gelir EarningAmount
/// demek" iki kez soylenirse iki kayit degil bir kayit olmali.
///
/// Sirket suzgeci her sorguda var. Baska bir sirketin baglanti kimligini
/// bilen biri onun ogrendiklerini ne okuyabilir ne silebilir.
/// </summary>
public class LearnedFactStore
{
    private readonly IMongoCollection<BsonDocument> _collection;
    private readonly ILogger<LearnedFactStore> _logger;

    public const string CollectionName = "ConnectionFacts";

    public LearnedFactStore(IMongoDatabase database, ILogger<LearnedFactStore> logger)
    {
        _collection = database.GetCollection<BsonDocument>(CollectionName);
        _logger = logger;
    }

    private static string DocumentId(Guid connectionId, string key) => $"{connectionId}:{key}";

    private static FilterDefinition<BsonDocument> Scoped(Guid connectionId, string companyId) =>
        Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("connectionId", connectionId.ToString()),
            Builders<BsonDocument>.Filter.Eq("companyId", companyId));

    /// <summary>
    /// Bir bilgiyi yazar ya da ustune yazar. Reddedilen bilgi de buradan
    /// gecer — <see cref="LearnedFact.Accepted"/> <c>false</c> olarak.
    /// </summary>
    public async Task SaveAsync(
        Guid connectionId, string companyId, LearnedFact fact, CancellationToken ct = default)
    {
        var update = Builders<BsonDocument>.Update
            .Set("connectionId", connectionId.ToString())
            .Set("companyId", companyId)
            .Set("key", fact.Key)
            .Set("kind", fact.Kind)
            .Set("accepted", fact.Accepted)
            .Set("fromTable", (BsonValue?)fact.FromTable ?? BsonNull.Value)
            .Set("fromColumn", (BsonValue?)fact.FromColumn ?? BsonNull.Value)
            .Set("toTable", (BsonValue?)fact.ToTable ?? BsonNull.Value)
            .Set("toColumn", (BsonValue?)fact.ToColumn ?? BsonNull.Value)
            .Set("table", (BsonValue?)fact.Table ?? BsonNull.Value)
            .Set("column", (BsonValue?)fact.Column ?? BsonNull.Value)
            .Set("value", (BsonValue?)fact.Value ?? BsonNull.Value)
            .Set("means", (BsonValue?)fact.Means ?? BsonNull.Value)
            .Set("question", (BsonValue?)fact.Question ?? BsonNull.Value)
            .Set("userId", (BsonValue?)fact.UserId ?? BsonNull.Value)
            .Set("createdAt", fact.CreatedAt);

        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", DocumentId(connectionId, fact.Key)),
            update,
            new UpdateOptions { IsUpsert = true },
            ct);

        _logger.LogInformation(
            "Öğrenildi ({Kind}, onay: {Accepted}). Connection: {ConnectionId}, kayıt: {Key}",
            fact.Kind, fact.Accepted, connectionId, fact.Key);
    }

    /// <summary>
    /// Bu baglantiya ait butun kayitlar — reddedilenler DAHIL.
    ///
    /// Reddedilenler cagirana lazim: "bir daha sorma" ancak hayir cevabi da
    /// okunursa uygulanabilir. Kullanmak isteyen taraf
    /// <see cref="LearnedFact.Accepted"/>'i kendisi suzer.
    /// </summary>
    public async Task<List<LearnedFact>> GetAllAsync(
        Guid connectionId, string companyId, CancellationToken ct = default)
    {
        var documents = await _collection
            .Find(Scoped(connectionId, companyId))
            .SortByDescending(d => d["createdAt"])
            .ToListAsync(ct);

        return documents.Select(Read).ToList();
    }

    /// <summary>
    /// Bir kaydin son "Analiz Et"te kurulup kurulamadigini yazar.
    ///
    /// <paramref name="problem"/> <c>null</c> ise kayit saglikli demektir ve
    /// varsa eski uyari temizlenir — duzelen bir sorunun ekranda asili
    /// kalmasi, hic uyarmamak kadar kotu.
    ///
    /// Kaydi SILMIYORUZ. Sema gecici olarak degismis olabilir (tablo secimden
    /// cikarilmis, sonra geri eklenmis); kullanicinin ogrettigi seyi onun
    /// haberi olmadan atmak, ogrenmeyi hic kaydetmemekten farksiz.
    /// </summary>
    public async Task SetStatusAsync(
        Guid connectionId, string companyId, string key, string? problem,
        CancellationToken ct = default)
    {
        var update = problem is null
            ? Builders<BsonDocument>.Update.Unset("problem").Unset("problemAt")
            : Builders<BsonDocument>.Update.Set("problem", problem).Set("problemAt", DateTime.UtcNow);

        await _collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", DocumentId(connectionId, key)),
                Builders<BsonDocument>.Filter.Eq("companyId", companyId)),
            update,
            // Upsert YOK: durum yalnizca var olan bir kayda yazilir. Aksi
            // halde silinmis bir kayit, ilk analizde govdesiz olarak geri
            // dogardi.
            cancellationToken: ct);
    }

    /// <summary>
    /// Tek bir kaydi siler. Kullanicinin yanlis ogretilmis bir bilgiden
    /// kurtulma yolu bu; onsuz bu ozelligin tamami gonderilmemeli.
    /// </summary>
    public async Task<bool> DeleteAsync(
        Guid connectionId, string companyId, string key, CancellationToken ct = default)
    {
        var result = await _collection.DeleteOneAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", DocumentId(connectionId, key)),
                // Sirket suzgeci silmede de var: kimligi bilmek yetmemeli.
                Builders<BsonDocument>.Filter.Eq("companyId", companyId)),
            ct);

        if (result.DeletedCount > 0)
            _logger.LogInformation(
                "Öğrenilen bilgi silindi. Connection: {ConnectionId}, kayıt: {Key}", connectionId, key);

        return result.DeletedCount > 0;
    }

    private static LearnedFact Read(BsonDocument doc) => new()
    {
        Key = Text(doc, "key") ?? "",
        Kind = Text(doc, "kind") ?? LearnedFact.Synonym,
        Accepted = doc.TryGetValue("accepted", out var a) && a.IsBoolean && a.AsBoolean,
        FromTable = Text(doc, "fromTable"),
        FromColumn = Text(doc, "fromColumn"),
        ToTable = Text(doc, "toTable"),
        ToColumn = Text(doc, "toColumn"),
        Table = Text(doc, "table"),
        Column = Text(doc, "column"),
        Value = Text(doc, "value"),
        Means = Text(doc, "means"),
        Question = Text(doc, "question"),
        Problem = Text(doc, "problem"),
        UserId = Text(doc, "userId"),
        CreatedAt = doc.TryGetValue("createdAt", out var c) && c.IsValidDateTime
            ? c.ToUniversalTime()
            : DateTime.UnixEpoch,
    };

    private static string? Text(BsonDocument doc, string name) =>
        doc.TryGetValue(name, out var value) && value.IsString ? value.AsString : null;
}

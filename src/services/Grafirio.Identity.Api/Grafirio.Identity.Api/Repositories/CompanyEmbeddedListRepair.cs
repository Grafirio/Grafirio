using Grafirio.Identity.Api.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Grafirio.Identity.Api.Repositories;

/// <summary>
/// Sonradan eklenen dizi alanlarını eski kayıtlara da yazar.
///
/// MongoDB şemasız ama EF sağlayıcısı değil: eşlenmiş bir dizi belgede hiç
/// yoksa okuma "Document element 'BankAccounts' is mapped collection but
/// missing" ile tamamen kırılıyor — alan nullable olsa bile, çünkü sorun
/// değerin null olması değil, elemanın hiç bulunmaması. Şirket Ayarları sayfası
/// üretimde tam olarak bu yüzden açılmadı.
///
/// Onarım EF ile değil sürücüyle yapılıyor: EF'in okuması zaten bu belgelerde
/// başarısız olduğu için kendi kendini düzeltemezdi.
///
/// Her açılışta çalışması güvenli: yalnızca eksik alanı olan belgelere dokunuyor.
/// </summary>
public static class CompanyEmbeddedListRepair
{
    /// <summary>
    /// Boş diziyle doldurulması yeterli olan alanlar: (koleksiyon, alan).
    /// Yeni bir gömülü liste eklendiğinde buraya da eklenmezse, alan eklenmeden
    /// önce yazılmış kayıtlar okunamaz hale gelir.
    /// </summary>
    private static readonly (string Collection, string Field)[] EmptyableListFields =
    [
        ("Companies", "Addresses"),
        ("Companies", "BankAccounts"),
        // Faz 2'de acilmis departmanlarda Modules alani yok.
        ("Departments", "Modules")
    ];

    public static async Task RepairCompanyEmbeddedListsExt(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var options = scope.ServiceProvider.GetRequiredService<MongoOption>();
        var database = client.GetDatabase(options.DatabaseName);

        foreach (var (collectionName, field) in EmptyableListFields)
        {
            var collection = database.GetCollection<BsonDocument>(collectionName);

            var result = await collection.UpdateManyAsync(
                Builders<BsonDocument>.Filter.Exists(field, false),
                Builders<BsonDocument>.Update.Set(field, new BsonArray()));

            if (result.ModifiedCount > 0)
            {
                app.Logger.LogInformation(
                    "{Collection} koleksiyonunda eksik '{Field}' alanı {Count} kayıtta boş diziyle dolduruldu.",
                    collectionName, field, result.ModifiedCount);
            }
        }

        await BackfillPathsAsync(database.GetCollection<BsonDocument>("Companies"), app.Logger);
    }

    /// <summary>
    /// <c>Path</c> için boş dizi yetmiyor: değeri hiyerarşiden hesaplanmalı.
    /// Alanı eksik ya da boş olan her şirket için kökten aşağı zincir kuruluyor.
    /// </summary>
    private static async Task BackfillPathsAsync(IMongoCollection<BsonDocument> companies, ILogger logger)
    {
        var all = await companies.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        if (all.Count == 0) return;

        var parentOf = new Dictionary<Guid, Guid?>();
        foreach (var doc in all)
        {
            var id = doc["_id"].AsGuid;
            parentOf[id] = doc.TryGetValue("ParentCompanyId", out var parent) && !parent.IsBsonNull
                ? parent.AsGuid
                : null;
        }

        var repaired = 0;

        foreach (var doc in all)
        {
            var hasPath = doc.TryGetValue("Path", out var existing)
                          && !existing.IsBsonNull
                          && existing.IsBsonArray
                          && existing.AsBsonArray.Count > 0;

            if (hasPath) continue;

            var id = doc["_id"].AsGuid;
            var path = BuildPath(id, parentOf);

            // Guid'ler ham tipleriyle degil BsonBinaryData olarak veriliyor:
            // surucunun varsayilan Guid gosterimi "Unspecified" oldugu icin ham
            // bir Guid'i serilestirmeyi reddediyor. Koleksiyondaki mevcut
            // kayitlar UuidStandard ile yazilmis, ayni gosterim kullaniliyor —
            // farkli olsaydi yazilan deger hicbir sorguyla eslesmezdi.
            await companies.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ToBson(id)),
                Builders<BsonDocument>.Update.Set("Path", new BsonArray(path.Select(ToBson))));

            repaired++;
        }

        if (repaired > 0)
        {
            logger.LogInformation("{Count} şirketin hiyerarşi yolu (Path) hesaplanıp yazıldı.", repaired);
        }
    }

    private static BsonBinaryData ToBson(Guid value) => new(value, GuidRepresentation.Standard);

    private static List<Guid> BuildPath(Guid id, Dictionary<Guid, Guid?> parentOf)
    {
        var chain = new List<Guid>();
        var current = (Guid?)id;

        // Bozuk bir veri döngü oluşturursa sonsuza kadar dönmesin: zincir
        // uzunluğu şirket sayısını aşamaz.
        var guard = parentOf.Count + 1;

        while (current.HasValue && guard-- > 0)
        {
            chain.Add(current.Value);
            current = parentOf.TryGetValue(current.Value, out var parent) ? parent : null;
        }

        chain.Reverse();
        return chain;
    }
}

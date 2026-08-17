using Grafirio.Identity.Api.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Grafirio.Identity.Api.Repositories;

/// <summary>
/// Şirket belgesine sonradan eklenen gömülü dizileri (adresler, banka hesapları)
/// eski kayıtlara da yazar.
///
/// MongoDB şemasız ama EF sağlayıcısı değil: <c>OwnsMany</c> ile eşlenmiş bir
/// dizi belgede hiç yoksa okuma "Document element 'BankAccounts' is mapped
/// collection but missing" ile tamamen kırılıyor — alan nullable olsa bile,
/// çünkü sorun değerin null olması değil, elemanın hiç bulunmaması. Alanlar
/// eklendikten sonra Şirket Ayarları sayfası uretimde tam olarak bu yüzden
/// açılmadı; aynı sınıf hata daha önce <see cref="Features.Companies.Company"/>
/// içindeki onay alanlarında da yaşanmıştı.
///
/// Onarım EF ile değil sürücüyle yapılıyor: EF'in okuması zaten bu belgelerde
/// başarısız olduğu için kendi kendini düzeltemezdi. BSON null da okunabiliyor
/// ama boş dizi yazılıyor; "hiç adres yok" ile "bilinmiyor" arasındaki farkı
/// koruyacak bir şey yok, boş dizi ikisini de doğru anlatıyor.
///
/// Her açılışta çalışması güvenli: yalnızca alanı bulunmayan belgelere dokunuyor,
/// dolayısıyla ikinci koşumda eşleşen belge kalmıyor.
/// </summary>
public static class CompanyEmbeddedListRepair
{
    private static readonly string[] EmbeddedListFields = ["Addresses", "BankAccounts"];

    public static async Task RepairCompanyEmbeddedListsExt(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var options = scope.ServiceProvider.GetRequiredService<MongoOption>();
        var companies = client.GetDatabase(options.DatabaseName).GetCollection<BsonDocument>("Companies");

        foreach (var field in EmbeddedListFields)
        {
            var missing = Builders<BsonDocument>.Filter.Exists(field, false);
            var setEmpty = Builders<BsonDocument>.Update.Set(field, new BsonArray());

            var result = await companies.UpdateManyAsync(missing, setEmpty);

            if (result.ModifiedCount > 0)
            {
                app.Logger.LogInformation(
                    "Şirket belgelerinde eksik '{Field}' alanı {Count} kayıtta boş diziyle dolduruldu.",
                    field, result.ModifiedCount);
            }
        }
    }
}

using MongoDB.Driver;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Gerçek Mongo. <c>BridgeStore</c> tamamen Mongo sorgularından oluşuyor;
/// sahte bir sürücüyle test etmek yalnızca sahtenin doğruluğunu ölçerdi.
///
/// Kimlik bilgisi bilerek AYRI alanlarda veriliyor: parola URI-güvensiz bir
/// karakter içerdiğinde bağlantı dizesi geçersiz oluyor ve hata "şifre yanlış"
/// değil "dize geçersiz" diyor. Üretimde de aynı yol kullanılıyor.
///
/// Konteyner yoksa testler atlanıyor:
///     docker compose up -d mongo.db
/// </summary>
public class MongoFixture : IAsyncLifetime
{
    private MongoClient? _client;

    public IMongoDatabase Database { get; private set; } = null!;

    public bool Available { get; private set; }

    public string SkipReason { get; private set; } = "";

    /// <summary>Her koşum kendi veritabanında: testler birbirini kirletmesin.</summary>
    private readonly string _databaseName = $"GrafirioBridgeTest_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        var username = DotEnv.Read("MONGO_USERNAME");
        var password = DotEnv.Read("MONGO_PASSWORD");

        if (username is null || password is null)
        {
            SkipReason = "MONGO_USERNAME/MONGO_PASSWORD okunamadı (.env yok).";
            return;
        }

        // Birden fazla aday deneniyor çünkü geliştirme makinelerinde 27017'yi
        // YEREL bir MongoDB kurulumu tutabiliyor. O durumda 127.0.0.1'e
        // bağlanmak sessizce YANLIŞ veritabanına gitmek olur; kimlik
        // doğrulaması tutmadığı için burada yakalanıyor ama sebebi
        // "şifre yanlış" gibi görünüyordu.
        string[] hosts = ["127.0.0.1:27017", "[::1]:27017"];
        var failures = new List<string>();

        foreach (var host in hosts)
        {
            try
            {
                var settings = MongoClientSettings.FromConnectionString(
                    $"mongodb://{host}/?authSource=admin");

                settings.Credential = MongoCredential.CreateCredential("admin", username, password);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);

                var client = new MongoClient(settings);
                var database = client.GetDatabase(_databaseName);

                // Gerçekten bağlanabildiğimizi doğrula: MongoClient tembel,
                // yapılandırma hatası ilk sorguda ortaya çıkıyor. `ping`
                // yetmez — kimlik doğrulaması gerektiren bir komut lazım,
                // yoksa yanlış sunucuya bağlanmış olmak fark edilmez.
                await database.RunCommandAsync<MongoDB.Bson.BsonDocument>(
                    new MongoDB.Bson.BsonDocument("listCollections", 1));

                _client = client;
                Database = database;
                Available = true;
                return;
            }
            catch (Exception ex)
            {
                failures.Add($"{host}: {ex.Message}");
            }
        }

        SkipReason =
            "Mongo'ya bağlanılamadı. 27017 portunu yerel bir MongoDB kurulumu " +
            "tutuyor olabilir; o zaman istekler Docker'daki sunucuya değil ona " +
            $"gider. Denenenler → {string.Join(" | ", failures)}";
    }

    public async Task DisposeAsync()
    {
        if (_client is not null && Available)
            await _client.DropDatabaseAsync(_databaseName);
    }
}

/// <summary>Depoda olmayan sırları .env'den okur; koda gömmek yerine.</summary>
public static class DotEnv
{
    public static string? Read(string key)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".env");

            if (File.Exists(candidate))
            {
                foreach (var line in File.ReadAllLines(candidate))
                {
                    if (!line.StartsWith(key + "=", StringComparison.Ordinal)) continue;
                    return line[(key.Length + 1)..].Trim().Trim('"');
                }
            }

            directory = directory.Parent;
        }

        return null;
    }
}

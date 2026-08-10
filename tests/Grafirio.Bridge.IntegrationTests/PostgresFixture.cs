using Grafirio.DataAnalysis.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.Bridge.IntegrationTests;

/// <summary>
/// Gerçek Postgres. <c>SavedConnection</c> kayıtları burada duruyor ve
/// <c>BridgeConnectionSync</c>'in okuduğu yer burası.
///
/// Şifreleme anahtarı da burada kuruluyor: <c>EncryptionHelper</c> anahtarı
/// statik ve tembel okuyor, yani ilk kullanımdan ÖNCE ayarlanmalı. Anahtar
/// yoksa testler atlanıyor — yanlış bir anahtarla koşmak, çözülemeyen
/// şifreler üretip sebebi anlaşılmayan hatalara yol açardı.
///
///     docker compose up -d postgres.db.dataanalysis
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=5433;Database=grafirio_dataanalysis;" +
        "Username=dataanalysis_user;Password=DataAnalysis123!;Timeout=5";

    public bool Available { get; private set; }

    public string SkipReason { get; private set; } = "";

    public DataAnalysisDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public async Task InitializeAsync()
    {
        var key = DotEnv.Read("ENCRYPTION_KEY");

        if (string.IsNullOrWhiteSpace(key))
        {
            SkipReason = "ENCRYPTION_KEY okunamadı (.env yok ya da tanımsız).";
            return;
        }

        // Statik ve tembel: ilk şifreleme çağrısından önce ayarlanmalı.
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", key);

        try
        {
            await using var db = CreateContext();
            await db.Database.EnsureCreatedAsync();
            await db.SavedConnections.AnyAsync();

            Available = true;
        }
        catch (Exception ex)
        {
            SkipReason = $"Postgres'e bağlanılamadı: {ex.Message}";
        }
    }

    /// <summary>
    /// Test kayıtlarını siler. Geliştirme veritabanı paylaşımlı: bırakılan
    /// satırlar panelde gerçek bağlantı gibi görünür ve kimin ne bıraktığı
    /// bir süre sonra anlaşılmaz olur.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (!Available) return;

        try
        {
            await using var db = CreateContext();
            await db.SavedConnections
                .Where(c => c.UserId == TestUserId)
                .ExecuteDeleteAsync();
        }
        catch
        {
            // Temizlik başarısızlığı testi kırmızıya çevirmemeli; sonuç
            // yalnızca birkaç artık satır.
        }
    }

    /// <summary>Testlerin yazdığı kayıtlar bu kullanıcıyla işaretleniyor.</summary>
    public const string TestUserId = "sync-test";
}

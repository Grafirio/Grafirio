using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Grafirio.DataAnalysis.Api.Data.Mongo;

/// <summary>
/// Kayitli bridge'ler ve hangi baglantinin hangi bridge'ten gectigi.
///
/// Neden Postgres degil de Mongo: Postgres semasi <c>EnsureCreated()</c> ile
/// kuruluyor ve migration yok — <see cref="Data.Entities.SavedConnection"/>'a
/// eklenen yeni bir kolon mevcut veritabanlarina yansimaz. Tablo secimi de
/// (<see cref="ConnectionProfileStore"/>) ayni sebeple burada.
///
/// Sifreler burada YOK. Bridge modundaki bir baglantinin kimlik bilgisi
/// musterinin kendi diskinde duruyor; bulut yalnizca hangi bridge'e sorulacagini
/// biliyor. Musteriye verilen sozun karsiligi bu.
/// </summary>
public class BridgeStore(IMongoDatabase database, ILogger<BridgeStore> logger)
    : Features.Bridge.IBridgePresence
{
    public const string BridgeCollectionName = "Bridges";
    public const string EnrollmentCollectionName = "BridgeEnrollments";
    public const string BindingCollectionName = "BridgeConnectionBindings";

    /// <summary>
    /// Kayit token'inin omru. Kisa: token, bir bridge'i sirkete baglayan tek
    /// sey; e-postayla dolasan ve haftalarca gecerli kalan bir sir olmamali.
    /// </summary>
    public static readonly TimeSpan EnrollmentTokenLifetime = TimeSpan.FromHours(2);

    private IMongoCollection<BsonDocument> Bridges =>
        database.GetCollection<BsonDocument>(BridgeCollectionName);

    private IMongoCollection<BsonDocument> Enrollments =>
        database.GetCollection<BsonDocument>(EnrollmentCollectionName);

    private IMongoCollection<BsonDocument> Bindings =>
        database.GetCollection<BsonDocument>(BindingCollectionName);

    /* ── Kayit token'i ────────────────────────────────────────────────── */

    /// <summary>
    /// Tek kullanimlik kayit token'i uretir. Token'in kendisi saklanmiyor,
    /// yalnizca ozeti — veritabanini okuyabilen biri bridge kaydedememeli.
    /// </summary>
    public async Task<string> CreateEnrollmentTokenAsync(
        string companyId, string createdByUserId, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "").Replace("/", "").Replace("=", "");

        await Enrollments.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Hash(token),
            ["companyId"] = companyId,
            ["createdByUserId"] = createdByUserId,
            ["createdAt"] = DateTime.UtcNow,
            ["expiresAt"] = DateTime.UtcNow.Add(EnrollmentTokenLifetime),
            ["usedAt"] = BsonNull.Value,
        }, cancellationToken: ct);

        logger.LogInformation("Bridge kayıt token'ı üretildi. Şirket: {CompanyId}", companyId);
        return token;
    }

    /// <summary>
    /// Token'i harcar ve bagli oldugu sirketi doner. Gecersiz, suresi dolmus
    /// veya kullanilmis token icin <c>null</c>.
    ///
    /// Harcama tek adimda ve kosullu: iki bridge ayni token'la ayni anda
    /// kaydolmaya calisirsa yalnizca biri gecer.
    /// </summary>
    public async Task<string?> RedeemEnrollmentTokenAsync(
        string token, CancellationToken ct = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", Hash(token)),
            Builders<BsonDocument>.Filter.Eq("usedAt", BsonNull.Value),
            Builders<BsonDocument>.Filter.Gt("expiresAt", DateTime.UtcNow));

        var document = await Enrollments.FindOneAndUpdateAsync(
            filter,
            Builders<BsonDocument>.Update.Set("usedAt", DateTime.UtcNow),
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
            ct);

        return document?.GetValue("companyId", BsonNull.Value).AsString;
    }

    /* ── Bridge kaydi ─────────────────────────────────────────────────── */

    /// <summary>
    /// Bridge'in defter kaydini acar.
    ///
    /// <b>Sir burada YOK.</b> Kimlik Keycloak'ta duruyor; buradaki kayit
    /// yalnizca panelin gosterdigi seyler icin: ad, makine, surum, son
    /// gorulme. Onceki surumde bu sinif kendi sirrini uretip SHA256 ozetini
    /// saklıyor ve sabit sureli karsilastirmayla dogruluyordu — auth sunucusu
    /// zaten kuruluyken elle yazilmis bir kimlik dogrulama katmaniydi.
    /// </summary>
    public async Task RegisterAsync(
        Guid bridgeId, string companyId, string name, string machineName, string version,
        CancellationToken ct = default)
    {
        await Bridges.InsertOneAsync(new BsonDocument
        {
            ["_id"] = bridgeId.ToString(),
            ["companyId"] = companyId,
            ["name"] = name,
            ["machineName"] = machineName,
            ["version"] = version,
            ["createdAt"] = DateTime.UtcNow,
            ["lastSeenAt"] = BsonNull.Value,
            ["revokedAt"] = BsonNull.Value,
        }, cancellationToken: ct);

        logger.LogInformation(
            "Bridge kaydedildi. Id: {BridgeId}, şirket: {CompanyId}, makine: {MachineName}",
            bridgeId, companyId, machineName);
    }

    /// <summary>
    /// Bridge bu sirkete ait ve iptal edilmemis mi. Kimligin KENDISI token'dan
    /// dogrulaniyor; burada yalnizca defter kontrolu var.
    /// </summary>
    public async Task<RegisteredBridge?> FindAsync(
        Guid bridgeId, CancellationToken ct = default)
    {
        var document = await Bridges
            .Find(Builders<BsonDocument>.Filter.Eq("_id", bridgeId.ToString()))
            .FirstOrDefaultAsync(ct);

        if (document is null) return null;
        if (document.GetValue("revokedAt", BsonNull.Value) != BsonNull.Value) return null;

        return Map(document);
    }

    public async Task<IReadOnlyList<RegisteredBridge>> ListAsync(
        string companyId, CancellationToken ct = default)
    {
        var documents = await Bridges
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("companyId", companyId),
                Builders<BsonDocument>.Filter.Eq("revokedAt", BsonNull.Value)))
            .ToListAsync(ct);

        return documents.Select(Map).ToList();
    }

    /// <inheritdoc cref="Features.Bridge.IBridgePresence.TouchAsync" />
    public async Task TouchAsync(Guid bridgeId, string version, CancellationToken ct = default) =>
        await Bridges.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", bridgeId.ToString()),
            Builders<BsonDocument>.Update
                .Set("lastSeenAt", DateTime.UtcNow)
                .Set("version", version),
            cancellationToken: ct);

    public async Task<bool> RevokeAsync(
        Guid bridgeId, string companyId, CancellationToken ct = default)
    {
        var result = await Bridges.UpdateOneAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", bridgeId.ToString()),
                Builders<BsonDocument>.Filter.Eq("companyId", companyId)),
            Builders<BsonDocument>.Update.Set("revokedAt", DateTime.UtcNow),
            cancellationToken: ct);

        return result.ModifiedCount > 0;
    }

    /* ── Baglanti → bridge eslemesi ───────────────────────────────────── */

    /// <summary>
    /// Bir kayitli baglantinin hangi bridge uzerinden okunacagini yazar.
    /// <paramref name="bridgeId"/> <c>null</c> ise baglanti dogrudan moda doner.
    /// </summary>
    public async Task BindConnectionAsync(
        Guid connectionId, string companyId, Guid? bridgeId, CancellationToken ct = default)
    {
        await Bindings.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", connectionId.ToString()),
            Builders<BsonDocument>.Update
                .Set("companyId", companyId)
                .Set("bridgeId", bridgeId?.ToString() ?? (BsonValue)BsonNull.Value)
                .Set("updatedAt", DateTime.UtcNow),
            new UpdateOptions { IsUpsert = true }, ct);

        logger.LogInformation(
            "Bağlantı {ConnectionId} artık {Mode} modunda.",
            connectionId, bridgeId is null ? "doğrudan" : $"bridge ({bridgeId})");
    }

    /// <summary>
    /// Baglanti bir bridge'e bagli mi. Bagli degilse <c>null</c> — yani
    /// dogrudan baglanti.
    /// </summary>
    public async Task<Guid?> GetBoundBridgeAsync(Guid connectionId, CancellationToken ct = default)
    {
        var document = await Bindings
            .Find(Builders<BsonDocument>.Filter.Eq("_id", connectionId.ToString()))
            .FirstOrDefaultAsync(ct);

        var value = document?.GetValue("bridgeId", BsonNull.Value);
        return value is null || value == BsonNull.Value ? null : Guid.Parse(value.AsString);
    }

    /// <summary>
    /// Sirketin butun baglanti eslemeleri, tek okumada.
    ///
    /// Panel her baglanti karti icin ayri bir istek atmasin diye toplu:
    /// yirmi baglantisi olan bir sirkette bu, ekran acilisinda yirmi cagri
    /// demek olurdu.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetBindingsAsync(
        string companyId, CancellationToken ct = default)
    {
        var documents = await Bindings
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("companyId", companyId),
                Builders<BsonDocument>.Filter.Ne("bridgeId", BsonNull.Value)))
            .ToListAsync(ct);

        return documents.ToDictionary(
            d => Guid.Parse(d["_id"].AsString),
            d => Guid.Parse(d["bridgeId"].AsString));
    }

    private static RegisteredBridge Map(BsonDocument document) => new(
        Guid.Parse(document["_id"].AsString),
        document.GetValue("companyId", "").AsString,
        document.GetValue("name", "").AsString,
        document.GetValue("machineName", "").AsString,
        document.GetValue("version", "").AsString,
        document.GetValue("createdAt", BsonNull.Value) is BsonDateTime created
            ? created.ToUniversalTime() : DateTime.MinValue,
        document.GetValue("lastSeenAt", BsonNull.Value) is BsonDateTime seen
            ? seen.ToUniversalTime() : null);

    private static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record RegisteredBridge(
    Guid Id,
    string CompanyId,
    string Name,
    string MachineName,
    string Version,
    DateTime CreatedAt,
    DateTime? LastSeenAt);

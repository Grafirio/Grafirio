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

    private IMongoCollection<BsonDocument> Bridges =>
        database.GetCollection<BsonDocument>(BridgeCollectionName);

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
}

public sealed record RegisteredBridge(
    Guid Id,
    string CompanyId,
    string Name,
    string MachineName,
    string Version,
    DateTime CreatedAt,
    DateTime? LastSeenAt);

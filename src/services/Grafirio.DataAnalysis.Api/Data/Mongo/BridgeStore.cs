using MongoDB.Bson;
using MongoDB.Driver;
using Grafirio.DataAnalysis.Api.Data.Access;

namespace Grafirio.DataAnalysis.Api.Data.Mongo;

/// <summary>
/// Kayitli bridge'ler ve hangi baglantinin hangi bridge'ten gectigi.
///
/// Neden Postgres degil de Mongo: Postgres semasi <c>EnsureCreated()</c> ile
/// kuruluyor ve migration yok — <see cref="Data.Entities.SavedConnection"/>'a
/// eklenen yeni bir kolon mevcut veritabanlarina yansimaz. Tablo secimi de
/// (<see cref="ConnectionProfileStore"/>) ayni sebeple burada.
///
/// Routing documents contain no credentials; encrypted saved credentials remain in PostgreSQL.
/// </summary>
public class BridgeStore(IMongoDatabase database, ILogger<BridgeStore> logger)
    : Features.Bridge.IBridgePresence
{
    public const string BridgeCollectionName = "Bridges";
    public const string RouteCollectionName = "ConnectionRoutes";

    private IMongoCollection<BsonDocument> Routes =>
        database.GetCollection<BsonDocument>(RouteCollectionName);

    public virtual async Task<ConnectionRoute> GetConnectionRouteAsync(
        Guid connectionId, string companyId, CancellationToken ct = default)
    {
        var document = await Routes.Find(RouteFilter(connectionId, companyId)).FirstOrDefaultAsync(ct);
        return document is null ? new ConnectionRoute() : new ConnectionRoute(
            document["connectionMode"].AsString,
            document["bridgeId"].IsBsonNull ? null : Guid.Parse(document["bridgeId"].AsString))
        {
            Fingerprint = document.GetValue("fingerprint", BsonNull.Value) is BsonString fingerprint ? fingerprint.Value : null,
            Revision = document.GetValue("revision", BsonNull.Value) is BsonString revision ? revision.Value : null,
            Pending = document.GetValue("pending", false).AsBoolean,
            Failed = document.GetValue("failed", false).AsBoolean
        };
    }

    public virtual async Task ReserveConnectionRouteAsync(
        Guid connectionId, string companyId, ConnectionRoute previous, string revision, CancellationToken ct = default)
    {
        if (previous.Pending && !previous.Failed)
            throw new DataSourceException("Bağlantının tamamlanmamış bir güncellemesi var; yönlendirme kapalı.");
        var filter = RouteFilter(connectionId, companyId) &
            Builders<BsonDocument>.Filter.Eq("revision", NullableString(previous.Revision)) &
            Builders<BsonDocument>.Filter.Eq("fingerprint", NullableString(previous.Fingerprint)) &
            Builders<BsonDocument>.Filter.Eq("connectionMode", previous.ConnectionMode) &
            Builders<BsonDocument>.Filter.Eq("bridgeId", previous.BridgeId is { } bridgeId ?
                (BsonValue)bridgeId.ToString() : BsonNull.Value) &
            (previous.Pending
                ? Builders<BsonDocument>.Filter.Eq("pending", true) & Builders<BsonDocument>.Filter.Eq("failed", true)
                : Builders<BsonDocument>.Filter.Ne("pending", true));
        try
        {
            var result = await Routes.UpdateOneAsync(filter,
                RouteUpdate(companyId, previous with { Pending = true, Failed = false, Revision = revision }),
                new UpdateOptions { IsUpsert = previous.Revision is null }, ct);
            if (result.MatchedCount == 0 && result.UpsertedId is null)
                throw new DataSourceException("Bağlantı başka bir istek tarafından güncellendi.");
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DataSourceException("Bağlantı başka bir istek tarafından güncelleniyor.", exception);
        }
    }

    public virtual async Task CompleteConnectionRouteAsync(
        Guid connectionId, string companyId, string revision, ConnectionRoute route, CancellationToken ct = default)
    {
        var filter = RouteFilter(connectionId, companyId) &
            Builders<BsonDocument>.Filter.Eq("revision", revision) &
            Builders<BsonDocument>.Filter.Eq("pending", true) &
            Builders<BsonDocument>.Filter.Ne("failed", true);
        var result = await Routes.UpdateOneAsync(filter, RouteUpdate(companyId, route), cancellationToken: ct);
        if (result.MatchedCount != 1)
            throw new DataSourceException("Bağlantı yönlendirme rezervasyonu değişti; işlem tamamlanamadı.");
    }

    public virtual async Task FailConnectionRouteAsync(
        Guid connectionId, string companyId, string revision, CancellationToken ct = default)
    {
        var filter = RouteFilter(connectionId, companyId) &
            Builders<BsonDocument>.Filter.Eq("revision", revision) &
            Builders<BsonDocument>.Filter.Eq("pending", true) &
            Builders<BsonDocument>.Filter.Ne("failed", true);
        var result = await Routes.UpdateOneAsync(filter,
            Builders<BsonDocument>.Update.Set("failed", true), cancellationToken: ct);
        if (result.MatchedCount != 1)
            throw new DataSourceException("Bağlantı rezervasyonu değişti; başarısız güncelleme işaretlenemedi.");
    }

    private static BsonValue NullableString(string? value) => value is null ? BsonNull.Value : new BsonString(value);

    private static UpdateDefinition<BsonDocument> RouteUpdate(string companyId, ConnectionRoute route) =>
        Builders<BsonDocument>.Update.Set("companyId", companyId)
            .Set("connectionMode", route.ConnectionMode)
            .Set("bridgeId", route.BridgeId is { } id ? (BsonValue)id.ToString() : BsonNull.Value)
            .Set("fingerprint", NullableString(route.Fingerprint))
            .Set("revision", NullableString(route.Revision))
            .Set("pending", route.Pending)
            .Set("failed", route.Failed);

    public virtual async Task SaveConnectionRouteAsync(
        Guid connectionId, string companyId, ConnectionRoute route, CancellationToken ct = default)
    {
        await ValidateRouteAsync(route, companyId, ct);
        // Route-only writes cannot establish a binding to the relational credential snapshot.
        throw new DataSourceException("Yönlendirme bağlantı kaydıyla birlikte kaydedilmelidir.");
    }

    public async Task ValidateRouteAsync(ConnectionRoute route, string companyId, CancellationToken ct = default)
    {
        route.Validate();
        if (route.ConnectionMode == ConnectionRoute.Direct) return;
        var bridge = await FindAsync(route.BridgeId!.Value, ct);
        if (bridge is null || bridge.CompanyId != companyId)
            throw new DataSourceException("Bridge bulunamadı veya bu şirkete ait değil.");
    }

    public async Task SetRequestedRouteAsync(Guid connectionId, string companyId,
        string? connectionMode, Guid? bridgeId, CancellationToken ct = default)
    {
        // An omitted route preserves the saved selection, never silently changing bridge to direct.
        var route = connectionMode is null && bridgeId is null
            ? await GetConnectionRouteAsync(connectionId, companyId, ct)
            : new ConnectionRoute(connectionMode ?? ConnectionRoute.Direct, bridgeId);
        await SaveConnectionRouteAsync(connectionId, companyId, route, ct);
    }

    private static FilterDefinition<BsonDocument> RouteFilter(Guid connectionId, string companyId) =>
        Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", connectionId.ToString()),
            Builders<BsonDocument>.Filter.Eq("companyId", companyId));

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
    public virtual async Task<RegisteredBridge?> FindAsync(
        Guid bridgeId, CancellationToken ct = default)
    {
        var document = await Bridges
            .Find(Builders<BsonDocument>.Filter.Eq("_id", bridgeId.ToString()))
            .FirstOrDefaultAsync(ct);

        if (document is null) return null;
        if (document.GetValue("revokedAt", BsonNull.Value) != BsonNull.Value) return null;

        return Map(document);
    }

    public virtual async Task<IReadOnlyList<RegisteredBridge>> ListAsync(
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

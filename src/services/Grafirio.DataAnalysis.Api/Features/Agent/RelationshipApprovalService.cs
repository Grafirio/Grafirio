using System.Text.Json;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Profile;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

public sealed class RelationshipApprovalService : IRelationshipApprovalService
{
    private const int SchemaTimeoutSeconds = 30;
    private const string RelationshipKeyPrefix = "rel:";
    private static readonly string[] OtherFactKeyPrefixes = ["syn:", "mean:", "code:", "label:"];
    private readonly IRelationshipDecisionPersistence _persistence;
    private readonly IDataSourceFactory _dataSources;
    private readonly RelationshipDiscovery _discovery;
    private readonly ILogger<RelationshipApprovalService> _logger;

    public RelationshipApprovalService(
        DataAnalysisDbContext db, IDataSourceFactory dataSources,
        RelationshipDiscovery discovery, LearnedFactStore facts,
        ILogger<RelationshipApprovalService> logger)
        : this(new RelationshipDecisionPersistence(db, facts), dataSources, discovery, logger)
    {
    }

    public RelationshipApprovalService(
        IRelationshipDecisionPersistence persistence, IDataSourceFactory dataSources,
        RelationshipDiscovery discovery, ILogger<RelationshipApprovalService> logger)
    {
        _persistence = persistence;
        _dataSources = dataSources;
        _discovery = discovery;
        _logger = logger;
    }

    public async Task<RelationshipApprovalResult> ApplyAsync(
        Guid connectionId, string companyId, LearnedFact fact, CancellationToken ct)
    {
        var connection = await _persistence.GetConnectionAsync(connectionId, companyId, ct);
        if (connection is null) return new(false, StatusCodes.Status404NotFound, "Bağlantı bulunamadı.");

        var config = await _persistence.GetActiveConfigAsync(connectionId, companyId, ct);
        if (config is null || config.Status != AgentAnalyzeEndpoints.AnalysisStatus.Ready)
            return new(false, StatusCodes.Status409Conflict, "Bağlantının analizi hazır değil; onay henüz uygulanmadı.");

        var selected = JsonSerializer.Deserialize<List<string>>(config.TablesJson) ?? [];
        if (!selected.Contains(fact.FromTable!, StringComparer.OrdinalIgnoreCase)
            || !selected.Contains(fact.ToTable!, StringComparer.OrdinalIgnoreCase))
            return new(false, StatusCodes.Status422UnprocessableEntity, "Eşleşmenin iki tablosu da mevcut analizde seçili olmalıdır.");

        RelationshipProfile? relationship = null;
        if (fact.Accepted)
        {
            try
            {
                await using var session = await _dataSources.OpenAsync(connection, ct);
                var tables = await ReadTablesAsync(session, [fact.FromTable!, fact.ToTable!], ct);
                var result = await _discovery.ValidateDeclaredAsync(session, tables,
                    new(fact.FromTable!, fact.FromColumn!, fact.ToTable!, fact.ToColumn!), ct);
                if (result.Relationship is null)
                    return new(false, StatusCodes.Status422UnprocessableEntity, result.Problem);
                relationship = result.Relationship;
            }
            catch (Exception exception) when (exception is DataSourceException or TimeoutException)
            {
                _logger.LogWarning(exception, "Relationship validation failed for connection {ConnectionId}", connectionId);
                return new(false, StatusCodes.Status422UnprocessableEntity,
                    "Veri kaynağına erişilip eşleşme doğrulanamadı. Onay uygulanmadı; yeniden deneyebilirsiniz.");
            }
        }

        var updatedJson = RelationshipDictionary.Apply(config.ConfigJson, fact, relationship);
        // These stores are not atomic: retain the complete fact for retry, but only CAS success means applied.
        await _persistence.SaveFactAsync(connectionId, companyId, fact, ct);
        if (!await _persistence.TryUpdateConfigAsync(config, updatedJson, true, ct))
        {
            _logger.LogWarning("Relationship fact recorded but not applied to config {ConfigId}: {Key}", config.Id, fact.Key);
            return new(false, StatusCodes.Status409Conflict,
                "Karar kalıcı belleğe kaydedildi ancak analiz değiştiği için güncel sözlüğe uygulanmadı. Yeniden deneyin.");
        }

        _logger.LogInformation("Relationship decision applied to active config {ConfigId}: {Key}, accepted {Accepted}",
            config.Id, fact.Key, fact.Accepted);
        return new(true, StatusCodes.Status200OK);
    }

    public async Task<RelationshipApprovalResult> ForgetAsync(
        Guid connectionId, string companyId, string key, CancellationToken ct)
    {
        if (await _persistence.GetConnectionAsync(connectionId, companyId, ct) is null)
            return new(false, StatusCodes.Status404NotFound, "Bağlantı bulunamadı.");

        var isRelationship = key.StartsWith(RelationshipKeyPrefix, StringComparison.Ordinal);
        if (!isRelationship && !OtherFactKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            return new(false, StatusCodes.Status400BadRequest, "Bilgi anahtarı geçersiz.");

        var config = await _persistence.GetActiveConfigAsync(connectionId, companyId, ct);
        var keepActive = isRelationship && config?.Status is
            AgentAnalyzeEndpoints.AnalysisStatus.Ready or AgentAnalyzeEndpoints.AnalysisStatus.AwaitingAnswers;
        var updatedJson = keepActive ? RelationshipDictionary.Forget(config!.ConfigJson, key) : config?.ConfigJson;

        await _persistence.DeleteFactAsync(connectionId, companyId, key, ct);

        if (config is not null)
        {
            if (!await _persistence.TryUpdateConfigAsync(config, updatedJson!, keepActive, ct))
                return ForgetConflict(key, connectionId);
        }
        else if (await _persistence.GetActiveConfigAsync(connectionId, companyId, ct) is not null
            || await _persistence.GetConnectionAsync(connectionId, companyId, ct) is null)
        {
            return ForgetConflict(key, connectionId);
        }

        _logger.LogInformation("Learned fact forgotten for connection {ConnectionId}: {Key}; active dictionary retained {KeepActive}",
            connectionId, key, keepActive);
        return new(true, StatusCodes.Status200OK);
    }

    private RelationshipApprovalResult ForgetConflict(string key, Guid connectionId)
    {
        _logger.LogWarning("Learned fact deleted but active dictionary cleanup conflicted for connection {ConnectionId}: {Key}",
            connectionId, key);
        return new(false, StatusCodes.Status409Conflict,
            "Bilgi kalıcı bellekten silindi ancak analiz değiştiği için sözlük temizliği tamamlanmadı. Silmeyi yeniden deneyin.");
    }

    private static async Task<List<TableProfile>> ReadTablesAsync(
        IDataSourceSession session, IReadOnlyList<string> names, CancellationToken ct)
    {
        // Catalog values, not model-generated identifiers, form the live validation profile.
        var rows = await session.QueryAsync<SchemaColumn>("""
            SELECT TABLE_SCHEMA AS [Schema], TABLE_NAME AS TableName,
                   COLUMN_NAME AS ColumnName, DATA_TYPE AS DataType
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA + '.' + TABLE_NAME IN @Names
            """, new { Names = names }, timeoutSeconds: SchemaTimeoutSeconds, ct: ct);
        return rows.GroupBy(row => (row.Schema, row.TableName)).Select(group => new TableProfile
        {
            Schema = group.Key.Schema,
            TableName = group.Key.TableName,
            Columns = group.Select(row => new ColumnProfile
            {
                ColumnName = row.ColumnName, DataType = row.DataType
            }).ToList()
        }).ToList();
    }

    private sealed class SchemaColumn
    {
        public string Schema { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
    }
}
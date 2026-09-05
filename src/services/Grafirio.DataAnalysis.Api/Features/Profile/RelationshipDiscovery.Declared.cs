using Grafirio.DataAnalysis.Api.Data.Access;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

public partial class RelationshipDiscovery
{
    private const int UniquenessQueryTimeoutSeconds = 30;

    /// <summary>
    /// Validates one declaration against the actual profile, target uniqueness and measured overlap.
    /// Does not discover foreign keys or infer other relationships.
    /// </summary>
    public async Task<(RelationshipProfile? Relationship, string? Problem)> ValidateDeclaredAsync(
        IDataSourceSession session,
        IReadOnlyList<TableProfile> tables,
        DeclaredLink link,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var problems = new List<DeclaredProblem>();
        var candidate = BuildResolvedDeclaredCandidates([link], tables, problems).SingleOrDefault();
        if (candidate is null) return (null, problems[0].Reason);

        Dictionary<string, HashSet<string>> uniqueColumns;
        try
        {
            uniqueColumns = await FetchUniqueColumnsAsync(
                session, [candidate.FromTable, candidate.ToTable], ct);
        }
        catch (Exception exception) when (IsVerificationFailure(exception, ct))
        {
            ct.ThrowIfCancellationRequested();
            logger.LogWarning(exception, "Unique metadata could not be verified for {Edge}", Key(candidate));
            return (null, UnverifiedTarget(candidate));
        }

        var result = await ValidateResolvedDeclaredAsync(session, tables, candidate, uniqueColumns, ct);
        if (result.Relationship is not null)
            AttachLabelColumns([result.Relationship], tables);
        return result;
    }

    /// <summary>
    /// Builds metadata-backed candidates without I/O. Missing metadata is not proof of duplicates;
    /// use <see cref="ValidateDeclaredAsync"/> to verify such declarations against live data.
    /// </summary>
    public List<RelationshipProfile> BuildDeclaredCandidates(
        IReadOnlyList<DeclaredLink>? links,
        IReadOnlyList<TableProfile> tables,
        IReadOnlyDictionary<string, HashSet<string>> uniqueColumns,
        ICollection<DeclaredProblem>? problems = null)
    {
        var result = new List<RelationshipProfile>();
        foreach (var candidate in BuildResolvedDeclaredCandidates(links, tables, problems))
        {
            if (!HasUniqueMetadata(uniqueColumns, candidate.ToTable, candidate.ToColumns[0]))
            {
                var reason = UnverifiedTarget(candidate);
                logger.LogWarning("Declared target requires data verification: {Edge}", Key(candidate));
                problems?.Add(new DeclaredProblem(LinkOf(candidate), reason));
                continue;
            }

            SetDeclaredCardinality(candidate, uniqueColumns);
            result.Add(candidate);
        }
        return result;
    }

    private async Task<(RelationshipProfile? Relationship, string? Problem)> ValidateResolvedDeclaredAsync(
        IDataSourceSession session,
        IReadOnlyList<TableProfile> tables,
        RelationshipProfile candidate,
        IReadOnlyDictionary<string, HashSet<string>> uniqueColumns,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!HasUniqueMetadata(uniqueColumns, candidate.ToTable, candidate.ToColumns[0]))
        {
            var problem = await VerifyDeclaredTargetAsync(session, tables, candidate, ct);
            if (problem is not null) return (null, problem);
        }

        SetDeclaredCardinality(candidate, uniqueColumns);
        var overlap = await MeasureOverlapAsync(session, candidate, tables, ct);
        if (overlap is null)
        {
            logger.LogWarning("Declared relationship overlap could not be measured: {Edge}", Key(candidate));
            return (null, "Bu iki kolon karşılaştırılamadı — tipleri uyuşmuyor olabilir.");
        }

        candidate.ValueOverlap = Math.Round(overlap.Value, 3);
        candidate.Confidence = overlap >= HighConfidenceOverlap ? "high" : "medium";
        // Low overlap warns for declarations; only inferred edges are rejected by this threshold.
        candidate.Note = overlap < MinOverlap
            ? $"Sizin kurduğunuz bağlantı. Değerlerin yalnızca %{overlap * 100:F0}'ı "
              + "hedef tabloda bulundu — sonuçlar eksik çıkabilir."
            : $"Sizin kurduğunuz bağlantı. Değerlerin %{overlap * 100:F0}'ı hedef tabloda bulundu.";
        logger.LogInformation("Declared relationship accepted ({Overlap:P0}): {Edge}", overlap, Key(candidate));
        return (candidate, null);
    }

    private async Task<string?> VerifyDeclaredTargetAsync(
        IDataSourceSession session,
        IReadOnlyList<TableProfile> tables,
        RelationshipProfile candidate,
        CancellationToken ct)
    {
        var target = tables.First(t => t.Qualified == candidate.ToTable);
        var column = Quote(candidate.ToColumns[0]);
        // Equality joins cannot match NULL, so repeated NULLs cannot multiply joined rows.
        var sql = $"""
            SELECT CAST(CASE WHEN EXISTS (
                SELECT {column}
                FROM {Quote(target.Schema)}.{Quote(target.TableName)}
                WHERE {column} IS NOT NULL
                GROUP BY {column}
                HAVING COUNT_BIG(*) > 1
            ) THEN 1 ELSE 0 END AS bit) AS HasDuplicates
            """;

        try
        {
            var result = await session.QueryFirstOrDefaultAsync<TargetUniquenessRow>(
                sql, timeoutSeconds: UniquenessQueryTimeoutSeconds, ct: ct);
            ct.ThrowIfCancellationRequested();
            if (result?.HasDuplicates is not { } hasDuplicates)
            {
                logger.LogWarning("Target uniqueness query returned no usable result: {Edge}", Key(candidate));
                return UnverifiedTarget(candidate);
            }
            if (!hasDuplicates) return null;

            logger.LogWarning("Declared target contains duplicate non-null values: {Edge}", Key(candidate));
            return $"{candidate.ToTable}.{candidate.ToColumns[0]} benzersiz değil: NULL olmayan "
                   + "tekrarlı değerler bulundu. Bu hedefe bağlanmak satırları çoğaltır ve "
                   + "toplamları şişirir; bu yüzden bağlantı kurulmadı.";
        }
        catch (Exception exception) when (IsVerificationFailure(exception, ct))
        {
            ct.ThrowIfCancellationRequested();
            logger.LogWarning(exception, "Declared target uniqueness could not be verified: {Edge}", Key(candidate));
            return UnverifiedTarget(candidate);
        }
    }

    private static bool IsVerificationFailure(Exception exception, CancellationToken ct) =>
        exception is DataSourceException or TimeoutException
        || exception is OperationCanceledException && !ct.IsCancellationRequested;

    private static string UnverifiedTarget(RelationshipProfile candidate) =>
        $"{candidate.ToTable}.{candidate.ToColumns[0]} hedefinin benzersizliği doğrulanamadı. "
        + "Benzersizlik güvencesi olmadan bağlantı kurulmadı; bu sonuç tekrarlı değer bulunduğu anlamına gelmez.";

    private static bool HasUniqueMetadata(
        IReadOnlyDictionary<string, HashSet<string>> uniqueColumns, string table, string column) =>
        uniqueColumns.TryGetValue(table, out var keys) && keys.Contains(column);

    private static void SetDeclaredCardinality(
        RelationshipProfile candidate, IReadOnlyDictionary<string, HashSet<string>> uniqueColumns) =>
        candidate.Cardinality = HasUniqueMetadata(uniqueColumns, candidate.FromTable, candidate.FromColumns[0])
            ? RelationshipProfile.OneToOne
            : RelationshipProfile.ManyToOne;

    private sealed class TargetUniquenessRow
    {
        public bool? HasDuplicates { get; set; }
    }
}
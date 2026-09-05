using Grafirio.DataAnalysis.Api.Data.Access;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Microsoft.Extensions.Logging.Abstractions;
using static Grafirio.DataAnalysis.Api.Features.Profile.RelationshipDiscovery;
using static Grafirio.DataAnalysis.Tests.FakeDataSourceSession;

namespace Grafirio.DataAnalysis.Tests;

public class DeclaredRelationshipValidationTests
{
    private const string UniqueMetadataQuery = "sys.indexes";
    private const string ForeignKeyQuery = "sys.foreign_keys";
    private const string UniquenessQuery = "AS HasDuplicates";
    private const string OverlapQuery = "AS Matched";
    private static readonly DeclaredLink Link = new("dbo.Movements", "F1", "dbo.Entities", "X_REF");

    private static RelationshipDiscovery Discovery() => new(NullLogger<RelationshipDiscovery>.Instance);

    private static List<TableProfile> Tables() =>
    [
        new()
        {
            Schema = "dbo", TableName = "Movements",
            Columns = [new() { ColumnName = "F1", DataType = "int" }]
        },
        new()
        {
            Schema = "dbo", TableName = "Entities",
            Columns =
            [
                new() { ColumnName = "X_REF", DataType = "int" },
                new() { ColumnName = "Name", DataType = "nvarchar", DistinctCount = 10 }
            ]
        }
    ];

    private static FakeDataSourceSession UnindexedSession(bool hasDuplicates = false, int matched = 100) =>
        new FakeDataSourceSession()
            .Respond(UniqueMetadataQuery)
            .Respond(ForeignKeyQuery)
            .Respond(UniquenessQuery, Row(("HasDuplicates", hasDuplicates)))
            .Respond(OverlapQuery, Row(("Total", 100), ("Matched", matched)));

    [Theory]
    [InlineData(100, "high")]
    [InlineData(75, "medium")]
    [InlineData(0, "medium")]
    public async Task Unindexed_declared_target_is_verified_without_naming_heuristics(int matched, string confidence)
    {
        var session = UnindexedSession(matched: matched);

        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(session, Tables(), Link);

        Assert.Null(problem);
        Assert.NotNull(relationship);
        Assert.Equal("declared", relationship.Source);
        Assert.Equal(RelationshipProfile.ManyToOne, relationship.Cardinality);
        Assert.True(relationship.IsOptional);
        Assert.False(relationship.IsTrusted);
        Assert.False(relationship.NeedsConfirmation);
        Assert.Equal(confidence, relationship.Confidence);
        Assert.Equal(matched / 100d, relationship.ValueOverlap);
        Assert.Equal("Name", relationship.LabelColumn);
        if (matched == 0) Assert.Contains("sonuçlar eksik çıkabilir", relationship.Note);
        Assert.Equal(3, session.ExecutedQueries.Count);
        Assert.DoesNotContain(session.ExecutedQueries, query => query.Contains(ForeignKeyQuery));
    }

    [Fact]
    public async Task Duplicate_target_is_rejected_before_overlap()
    {
        var session = UnindexedSession(hasDuplicates: true);
        var tables = Tables();
        tables[1].ApproximateRowCount = 100;
        tables[1].SampledRowCount = 100;
        tables[1].Columns[0].DistinctCount = 100;
        tables[1].Columns[0].StatsFromSample = true;

        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(session, tables, Link);

        Assert.Null(relationship);
        Assert.Contains("benzersiz değil", problem);
        Assert.Contains("NULL olmayan tekrarlı değerler", problem);
        Assert.DoesNotContain("doğrulanamadı", problem);
        Assert.DoesNotContain(session.ExecutedQueries, query => query.Contains(OverlapQuery));
    }

    [Fact]
    public async Task Uniqueness_checks_all_non_null_values_without_sampling()
    {
        var session = UnindexedSession();
        await Discovery().ValidateDeclaredAsync(session, Tables(), Link);

        var query = Assert.Single(session.ExecutedQueries, sql => sql.Contains(UniquenessQuery));
        Assert.Contains("FROM [dbo].[Entities]", query);
        Assert.Contains("WHERE [X_REF] IS NOT NULL", query);
        Assert.Contains("GROUP BY [X_REF]", query);
        Assert.Contains("HAVING COUNT_BIG(*) > 1", query);
        Assert.DoesNotContain("TOP", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TABLESAMPLE", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NOLOCK", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Movements", query);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trusted_metadata_skips_data_uniqueness_check_and_preserves_cardinality(bool sourceIsUnique)
    {
        var session = new FakeDataSourceSession()
            .Respond(UniqueMetadataQuery,
                Row(("TableName", Link.ToTable), ("ColumnName", Link.ToColumn)),
                Row(("TableName", Link.FromTable), ("ColumnName", sourceIsUnique ? Link.FromColumn : "Other")))
            .Respond(OverlapQuery, Row(("Total", 100), ("Matched", 100)));

        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(session, Tables(), Link);

        Assert.Null(problem);
        Assert.NotNull(relationship);
        Assert.Equal(sourceIsUnique ? RelationshipProfile.OneToOne : RelationshipProfile.ManyToOne,
            relationship.Cardinality);
        Assert.DoesNotContain(session.ExecutedQueries, query => query.Contains(UniquenessQuery));
        var metadataQuery = session.ExecutedQueries[0];
        Assert.Contains("i.has_filter = 0", metadataQuery);
        Assert.Contains("i.is_disabled = 0", metadataQuery);
        Assert.Contains("i.is_hypothetical = 0", metadataQuery);
        Assert.Contains("ic.is_included_column = 0", metadataQuery);
        Assert.Contains("HAVING COUNT(*) = 1", metadataQuery);
    }

    [Theory]
    [InlineData("dbo.Missing", "F1", "dbo.Entities", "X_REF")]
    [InlineData("dbo.Movements", "F1", "dbo.Missing", "X_REF")]
    [InlineData("dbo.Movements", "Missing", "dbo.Entities", "X_REF")]
    [InlineData("dbo.Movements", "F1", "dbo.Entities", "Missing")]
    [InlineData("dbo.Movements", "F1", "dbo.Movements", "F1")]
    [InlineData("dbo.Movements", "F1", "dbo.Entities]; DROP TABLE Entities;--", "X_REF")]
    [InlineData("dbo.Movements", "F1", "dbo.Entities", "X_REF]; DROP TABLE Entities;--")]
    public async Task Invalid_profile_identifiers_are_rejected_before_any_query(
        string fromTable, string fromColumn, string toTable, string toColumn)
    {
        var session = new FakeDataSourceSession();
        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(
            session, Tables(), new(fromTable, fromColumn, toTable, toColumn));

        Assert.Null(relationship);
        Assert.NotNull(problem);
        Assert.Empty(session.ExecutedQueries);
    }

    [Fact]
    public async Task Failed_profile_table_is_not_usable()
    {
        var tables = Tables();
        tables[1].Error = "Profile unavailable";
        var session = new FakeDataSourceSession();

        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(session, tables, Link);

        Assert.Null(relationship);
        Assert.Contains("tablosu", problem);
        Assert.Empty(session.ExecutedQueries);
    }

    [Fact]
    public async Task Resolved_schema_table_and_columns_are_quoted_as_separate_identifiers()
    {
        var tables = Tables();
        tables[0].Schema = "sales.data";
        tables[0].TableName = "Move]ments";
        tables[0].Columns[0].ColumnName = "F]1";
        tables[1].Schema = "lookup.schema]";
        tables[1].TableName = "Entities.archive]";
        tables[1].Columns[0].ColumnName = "X].REF";
        var link = new DeclaredLink(tables[0].Qualified.ToUpperInvariant(), "f]1",
            tables[1].Qualified.ToUpperInvariant(), "x].ref");
        var session = UnindexedSession();

        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(session, tables, link);

        Assert.Null(problem);
        Assert.NotNull(relationship);
        Assert.Equal(tables[1].Qualified, relationship.ToTable);
        var uniqueness = Assert.Single(session.ExecutedQueries, sql => sql.Contains(UniquenessQuery));
        Assert.Contains("FROM [lookup.schema]]].[Entities.archive]]]", uniqueness);
        Assert.Contains("WHERE [X]].REF] IS NOT NULL", uniqueness);
        var overlap = Assert.Single(session.ExecutedQueries, sql => sql.Contains(OverlapQuery));
        Assert.Contains("FROM [sales.data].[Move]]ments]", overlap);
        Assert.Contains("[F]]1] IS NOT NULL", overlap);
        Assert.Contains("FROM [lookup.schema]]].[Entities.archive]]]", overlap);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_null_uniqueness_result_is_not_proof_of_uniqueness(bool nullFlag)
    {
        var session = new FakeDataSourceSession().Respond(UniqueMetadataQuery);
        if (nullFlag) session.Respond(UniquenessQuery, Row(("HasDuplicates", null)));
        else session.Respond(UniquenessQuery);

        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(session, Tables(), Link);

        Assert.Null(relationship);
        Assert.Contains("benzersizliği doğrulanamadı", problem);
        Assert.DoesNotContain("benzersiz değil", problem);
        Assert.DoesNotContain(session.ExecutedQueries, query => query.Contains(OverlapQuery));
    }

    [Theory]
    [InlineData("data")]
    [InlineData("timeout")]
    [InlineData("internal-cancellation")]
    [InlineData("metadata")]
    public async Task Verification_failure_is_reported_separately_from_duplicates(string failure)
    {
        var inner = UnindexedSession();
        var session = new InspectingSession(inner, (sql, _, _) =>
        {
            if (sql.Contains(failure == "metadata" ? UniqueMetadataQuery : UniquenessQuery))
                throw failure switch
                {
                    "timeout" => new TimeoutException("Timed out"),
                    "internal-cancellation" => new OperationCanceledException(),
                    _ => new DataSourceException("Permission denied")
                };
        });

        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(session, Tables(), Link);

        Assert.Null(relationship);
        Assert.Contains("benzersizliği doğrulanamadı", problem);
        Assert.DoesNotContain("benzersiz değil", problem);
        Assert.DoesNotContain(inner.ExecutedQueries, sql => sql.Contains(OverlapQuery));
    }

    [Fact]
    public async Task Target_verification_has_bounded_timeout_and_receives_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var inspected = false;
        var session = new InspectingSession(UnindexedSession(), (sql, timeout, token) =>
        {
            if (!sql.Contains(UniquenessQuery)) return;
            inspected = true;
            Assert.Equal(30, timeout);
            Assert.Equal(cancellation.Token, token);
        });

        await Discovery().ValidateDeclaredAsync(session, Tables(), Link, cancellation.Token);
        Assert.True(inspected);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_not_reported_as_duplicates()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new InspectingSession(UnindexedSession(), (sql, _, token) =>
        {
            if (!sql.Contains(UniquenessQuery)) return;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Discovery().ValidateDeclaredAsync(session, Tables(), Link, cancellation.Token));
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(false, 0)]
    [InlineData(true, 100)]
    public async Task Discovery_and_single_validation_share_uniqueness_and_overlap_gates(bool duplicates, int matched)
    {
        var discoverySession = UnindexedSession(duplicates, matched);
        var problems = new List<DeclaredProblem>();
        var edges = await Discovery().DiscoverAsync(discoverySession, Tables(), [Link], problems: problems);
        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(
            UnindexedSession(duplicates, matched), Tables(), Link);

        if (duplicates)
        {
            Assert.Empty(edges);
            Assert.Null(relationship);
            Assert.Equal(problem, Assert.Single(problems).Reason);
        }
        else
        {
            var edge = Assert.Single(edges);
            Assert.NotNull(relationship);
            Assert.Empty(problems);
            Assert.Equal(relationship.Source, edge.Source);
            Assert.Equal(relationship.ValueOverlap, edge.ValueOverlap);
            Assert.Equal(relationship.Note, edge.Note);
            Assert.Equal(relationship.Confidence, edge.Confidence);
            Assert.Equal(relationship.LabelColumn, edge.LabelColumn);
        }
        Assert.Single(discoverySession.ExecutedQueries, sql => sql.Contains(UniquenessQuery));
    }

    [Fact]
    public async Task Discovery_does_not_scan_unindexed_targets_without_declarations()
    {
        var session = new FakeDataSourceSession().Respond(UniqueMetadataQuery).Respond(ForeignKeyQuery);
        var tables = Tables();
        tables[0].Columns[0].ColumnName = "EntityId";
        tables[1].Columns[0].ColumnName = "Id";

        Assert.Empty(await Discovery().DiscoverAsync(session, tables));
        Assert.Equal(2, session.ExecutedQueries.Count);
    }

    [Fact]
    public async Task Data_verified_target_is_not_promoted_to_inference_metadata()
    {
        var session = UnindexedSession();
        var tables = Tables();
        tables[0].Columns.Add(new() { ColumnName = "EntityId", DataType = "int" });
        tables[1].Columns[0].ColumnName = "Id";

        var edges = await Discovery().DiscoverAsync(session, tables, [Link with { ToColumn = "Id" }]);

        var edge = Assert.Single(edges);
        Assert.Equal("declared", edge.Source);
        Assert.Equal("F1", Assert.Single(edge.FromColumns));
        Assert.Single(session.ExecutedQueries, sql => sql.Contains(UniquenessQuery));
        Assert.Single(session.ExecutedQueries, sql => sql.Contains(OverlapQuery));
    }

    [Fact]
    public async Task Discovery_reports_unverifiable_declared_target()
    {
        var inner = UnindexedSession();
        var session = new InspectingSession(inner, (sql, _, _) =>
        {
            if (sql.Contains(UniquenessQuery)) throw new DataSourceException("Permission denied");
        });
        var problems = new List<DeclaredProblem>();

        Assert.Empty(await Discovery().DiscoverAsync(session, Tables(), [Link], problems: problems));

        var problem = Assert.Single(problems);
        Assert.Equal(Link, problem.Link);
        Assert.Contains("benzersizliği doğrulanamadı", problem.Reason);
        Assert.DoesNotContain(inner.ExecutedQueries, sql => sql.Contains(OverlapQuery));
    }

    [Fact]
    public async Task Discovery_reports_missing_profile_without_querying()
    {
        var session = new FakeDataSourceSession();
        var problems = new List<DeclaredProblem>();

        Assert.Empty(await Discovery().DiscoverAsync(session, [], [Link], problems: problems));

        Assert.Contains("tablosu", Assert.Single(problems).Reason);
        Assert.Empty(session.ExecutedQueries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task Unmeasurable_overlap_remains_a_rejection(int total)
    {
        var inner = new FakeDataSourceSession()
            .Respond(UniqueMetadataQuery)
            .Respond(UniquenessQuery, Row(("HasDuplicates", false)))
            .Respond(OverlapQuery, Row(("Total", total), ("Matched", 0)));
        var session = new InspectingSession(inner, (sql, _, _) =>
        {
            if (total > 0 && sql.Contains(OverlapQuery)) throw new DataSourceException("Incompatible types");
        });

        var (relationship, problem) = await Discovery().ValidateDeclaredAsync(session, Tables(), Link);

        Assert.Null(relationship);
        Assert.Contains("karşılaştırılamadı", problem);
    }

    private sealed class InspectingSession(
        FakeDataSourceSession inner, Action<string, int?, CancellationToken> inspect) : IDataSourceSession
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(
            string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default)
        {
            inspect(sql, timeoutSeconds, ct);
            return inner.QueryAsync<T>(sql, parameters, timeoutSeconds, ct);
        }

        public Task<IReadOnlyList<QueryRow>> QueryRowsAsync(
            string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default) =>
            inner.QueryRowsAsync(sql, parameters, timeoutSeconds, ct);

        public Task<T?> ScalarAsync<T>(
            string sql, object? parameters = null, int? timeoutSeconds = null, CancellationToken ct = default) =>
            inner.ScalarAsync<T>(sql, parameters, timeoutSeconds, ct);

        public IAsyncEnumerable<QueryRow> StreamAsync(
            string sql, object? parameters = null, int? timeoutSeconds = null, int? maxRows = null,
            CancellationToken ct = default) => inner.StreamAsync(sql, parameters, timeoutSeconds, maxRows, ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
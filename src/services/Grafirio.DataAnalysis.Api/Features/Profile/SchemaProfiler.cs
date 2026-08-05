using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>
/// Secili tablolarin profilini cikarir: sema, anahtarlar, kolon istatistikleri
/// ve — politikanin izin verdigi olcude — ornek degerler.
///
/// Kural: <b>yalnizca secili tablolar</b>. Her sorgu tablo listesine kisitli;
/// secili olmayan bir tabloya tek bir okuma bile gitmez.
///
/// Neden ornek deger: kolonun adi ne oldugunu soylemiyor.
/// `ReceiverCompanyCountryName` adindan "ulke" oldugu cikmayabilir, ama
/// icinde "Almanya", "Hollanda" gorununce belli oluyor. Esleme kalitesini
/// belirleyen asil sinyal bu.
/// </summary>
public class SchemaProfiler(ILogger<SchemaProfiler> logger)
{
    private const int SampleSize = 20;
    private const int LowCardinalityThreshold = 50;

    public async Task<DatabaseProfile> ProfileAsync(
        string connectionString,
        string databaseName,
        IReadOnlyList<string> selectedTables,
        bool samplingConsentGiven,
        CancellationToken ct = default)
    {
        if (selectedTables.Count == 0)
            throw new InvalidOperationException("Tablo seçimi boş; profil çıkarılamaz.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var profile = new DatabaseProfile
        {
            DatabaseName = databaseName,
            SamplingConsentGiven = samplingConsentGiven
        };

        foreach (var qualified in selectedTables)
        {
            var (schema, table) = SplitTableName(qualified);
            try
            {
                profile.Tables.Add(await ProfileTableAsync(
                    connection, schema, table, samplingConsentGiven, ct));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Tablo profillenemedi: {Schema}.{Table}", schema, table);
                profile.Tables.Add(new TableProfile
                {
                    Schema = schema,
                    TableName = table,
                    Error = ex.Message
                });
            }
        }

        profile.Relationships = await FetchRelationshipsAsync(connection, selectedTables, ct);
        return profile;
    }

    private async Task<TableProfile> ProfileTableAsync(
        SqlConnection connection, string schema, string table, bool consent, CancellationToken ct)
    {
        var result = new TableProfile { Schema = schema, TableName = table };

        // Yaklasik satir sayisi. COUNT(*) her tabloda tam tarama demekti;
        // profil icin kesin sayiya ihtiyac yok.
        result.ApproximateRowCount = await connection.ExecuteScalarAsync<long?>(
            new CommandDefinition(@"
                SELECT SUM(p.rows)
                FROM sys.partitions p
                JOIN sys.objects o ON o.object_id = p.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE s.name = @Schema AND o.name = @Table AND p.index_id IN (0, 1)",
                new { Schema = schema, Table = table }, cancellationToken: ct)) ?? 0;

        var columns = (await connection.QueryAsync<ColumnRow>(
            new CommandDefinition(@"
                SELECT c.COLUMN_NAME AS ColumnName, c.DATA_TYPE AS DataType,
                       CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                       c.CHARACTER_MAXIMUM_LENGTH AS MaxLength
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @Table
                ORDER BY c.ORDINAL_POSITION",
                new { Schema = schema, Table = table }, cancellationToken: ct))).ToList();

        var primaryKeys = (await connection.QueryAsync<string>(
            new CommandDefinition(@"
                SELECT k.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
                  ON k.CONSTRAINT_NAME = t.CONSTRAINT_NAME AND k.TABLE_SCHEMA = t.TABLE_SCHEMA
                WHERE t.CONSTRAINT_TYPE = 'PRIMARY KEY'
                  AND t.TABLE_SCHEMA = @Schema AND t.TABLE_NAME = @Table",
                new { Schema = schema, Table = table }, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            var stats = await FetchColumnStatsAsync(connection, schema, table, column, ct);

            var decision = SensitiveColumnPolicy.Evaluate(
                column.ColumnName, column.DataType, stats.DistinctCount, LowCardinalityThreshold);

            var profile = new ColumnProfile
            {
                ColumnName = column.ColumnName,
                DataType = column.DataType,
                IsNullable = column.IsNullable,
                MaxLength = column.MaxLength,
                IsPrimaryKey = primaryKeys.Contains(column.ColumnName),
                DistinctCount = stats.DistinctCount,
                NullCount = stats.NullCount,
                MinValue = stats.MinValue,
                MaxValue = stats.MaxValue,
                SamplingDecision = decision.Decision.ToString(),
                SamplingNote = decision.Reason
            };

            var maySample = decision.Decision == SensitiveColumnPolicy.Decision.Allowed
                            || (decision.Decision == SensitiveColumnPolicy.Decision.NeedsConsent && consent);

            if (maySample)
                profile.SampleValues = await FetchSamplesAsync(connection, schema, table, column.ColumnName, ct);

            result.Columns.Add(profile);
        }

        return result;
    }

    private static async Task<ColumnStats> FetchColumnStatsAsync(
        SqlConnection connection, string schema, string table, ColumnRow column, CancellationToken ct)
    {
        // Kolon adi parametre olarak gecirilemez (tanimlayici), bu yuzden
        // koseli parantez icine alinip icindeki ] iki katina cikariliyor —
        // SQL Server'in tanimlayici kacisi budur.
        var col = Quote(column.ColumnName);
        var tbl = $"{Quote(schema)}.{Quote(table)}";

        var isComparable = column.DataType.ToLowerInvariant() is not ("text" or "ntext" or "image" or "xml" or "geography" or "geometry");

        var sql = isComparable
            ? $"SELECT COUNT(DISTINCT {col}) AS DistinctCount, SUM(CASE WHEN {col} IS NULL THEN 1 ELSE 0 END) AS NullCount, CAST(MIN({col}) AS NVARCHAR(200)) AS MinValue, CAST(MAX({col}) AS NVARCHAR(200)) AS MaxValue FROM {tbl}"
            : $"SELECT NULL AS DistinctCount, SUM(CASE WHEN {col} IS NULL THEN 1 ELSE 0 END) AS NullCount, NULL AS MinValue, NULL AS MaxValue FROM {tbl}";

        try
        {
            return await connection.QuerySingleAsync<ColumnStats>(
                new CommandDefinition(sql, cancellationToken: ct));
        }
        catch
        {
            // Tek bir kolonun istatistigi alinamadi diye profil komple
            // dusmesin; o kolon istatistiksiz devam eder.
            return new ColumnStats();
        }
    }

    private static async Task<List<string>> FetchSamplesAsync(
        SqlConnection connection, string schema, string table, string columnName, CancellationToken ct)
    {
        var col = Quote(columnName);
        var tbl = $"{Quote(schema)}.{Quote(table)}";
        var sql = $"SELECT DISTINCT TOP {SampleSize} CAST({col} AS NVARCHAR(200)) AS Value FROM {tbl} WHERE {col} IS NOT NULL";

        try
        {
            var values = await connection.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
            // Kolon adi masum olsa bile icerigi hassas desene uyan degerler elenir.
            return values
                .Where(SensitiveColumnPolicy.IsValueSafe)
                .Take(SampleSize)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static async Task<List<RelationshipProfile>> FetchRelationshipsAsync(
        SqlConnection connection, IReadOnlyList<string> selectedTables, CancellationToken ct)
    {
        var names = selectedTables.Select(t => SplitTableName(t).Table).Distinct().ToList();

        var rows = await connection.QueryAsync<RelationshipProfile>(
            new CommandDefinition(@"
                SELECT
                    OBJECT_NAME(fk.parent_object_id)     AS FromTable,
                    pc.name                              AS FromColumn,
                    OBJECT_NAME(fk.referenced_object_id) AS ToTable,
                    rc.name                              AS ToColumn
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
                JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
                WHERE OBJECT_NAME(fk.parent_object_id) IN @Names
                  AND OBJECT_NAME(fk.referenced_object_id) IN @Names",
                new { Names = names }, cancellationToken: ct));

        return rows.ToList();
    }

    private static (string Schema, string Table) SplitTableName(string qualified)
    {
        var cleaned = qualified.Replace("[", "").Replace("]", "").Trim();
        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? (parts[0], parts[^1]) : ("dbo", cleaned);
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    private sealed class ColumnRow
    {
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool IsNullable { get; set; }
        public int? MaxLength { get; set; }
    }

    private sealed class ColumnStats
    {
        public int? DistinctCount { get; set; }
        public long? NullCount { get; set; }
        public string? MinValue { get; set; }
        public string? MaxValue { get; set; }
    }
}

public class DatabaseProfile
{
    public string DatabaseName { get; set; } = "";
    public bool SamplingConsentGiven { get; set; }
    public List<TableProfile> Tables { get; set; } = [];
    public List<RelationshipProfile> Relationships { get; set; } = [];
}

public class TableProfile
{
    public string Schema { get; set; } = "";
    public string TableName { get; set; } = "";
    public long ApproximateRowCount { get; set; }
    public List<ColumnProfile> Columns { get; set; } = [];
    public string? Error { get; set; }
}

public class ColumnProfile
{
    public string ColumnName { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public bool IsPrimaryKey { get; set; }
    public int? DistinctCount { get; set; }
    public long? NullCount { get; set; }
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public List<string> SampleValues { get; set; } = [];
    public string SamplingDecision { get; set; } = "";
    public string? SamplingNote { get; set; }
}

public class RelationshipProfile
{
    public string FromTable { get; set; } = "";
    public string FromColumn { get; set; } = "";
    public string ToTable { get; set; } = "";
    public string ToColumn { get; set; } = "";
}

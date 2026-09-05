using Grafirio.DataAnalysis.Api.Data.Access;

namespace Grafirio.DataAnalysis.Api.Features.Profile;

/// <summary>Reads declared foreign keys without sampling customer values.</summary>
internal static class MetadataRelationships
{
    public static async Task<List<RelationshipProfile>> ReadAsync(
        IDataSourceSession session, IReadOnlyList<TableProfile> tables, CancellationToken ct)
    {
        var names = tables.Where(table => table.Error is null).Select(table => table.Qualified).ToArray();
        var rows = await session.QueryAsync<ForeignKeyMetadata>("""
            SELECT fk.object_id AS ConstraintId,
                   ps.name + '.' + po.name AS FromTable, pc.name AS FromColumn,
                   rs.name + '.' + ro.name AS ToTable, rc.name AS ToColumn,
                   fk.is_not_trusted AS IsNotTrusted, pc.is_nullable AS IsNullable,
                   fkc.constraint_column_id AS Ordinal
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.objects po ON po.object_id = fk.parent_object_id
            JOIN sys.schemas ps ON ps.schema_id = po.schema_id
            JOIN sys.objects ro ON ro.object_id = fk.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE ps.name + '.' + po.name IN @Names AND rs.name + '.' + ro.name IN @Names
            ORDER BY fk.object_id, fkc.constraint_column_id
            """, new { Names = names }, ct: ct);
        return rows.GroupBy(row => row.ConstraintId).Select(group => new RelationshipProfile
        {
            FromTable = group.First().FromTable,
            FromColumns = group.OrderBy(row => row.Ordinal).Select(row => row.FromColumn).ToList(),
            ToTable = group.First().ToTable,
            ToColumns = group.OrderBy(row => row.Ordinal).Select(row => row.ToColumn).ToList(),
            IsOptional = group.Any(row => row.IsNullable || row.IsNotTrusted),
            IsTrusted = !group.Any(row => row.IsNotTrusted),
            Source = "fk", Confidence = "high"
        }).ToList();
    }

    private sealed class ForeignKeyMetadata
    {
        public int ConstraintId { get; set; }
        public string FromTable { get; set; } = "";
        public string FromColumn { get; set; } = "";
        public string ToTable { get; set; } = "";
        public string ToColumn { get; set; } = "";
        public bool IsNotTrusted { get; set; }
        public bool IsNullable { get; set; }
        public int Ordinal { get; set; }
    }
}
using System.Data;
using System.Data.Common;
using SqlPolicy = Grafirio.QueryPolicy.QueryPolicy;

namespace Grafirio.Bridge.Desktop.LocalWorkspace.Data;

public static class SqlServerDiscovery
{
    private const string MetadataSql = """
        SELECT c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE
        FROM INFORMATION_SCHEMA.COLUMNS c
        INNER JOIN INFORMATION_SCHEMA.TABLES t
            ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
        WHERE t.TABLE_TYPE = 'BASE TABLE' AND ({0})
        ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
        """;
    private const string DenyAll = "1 = 0";

    public static void Configure(DbCommand command, IReadOnlyCollection<string> allowedTables)
    {
        if (allowedTables.Count > WorkspaceLimits.MaxAllowedTables)
            throw new ArgumentException("Too many allowed tables.", nameof(allowedTables));
        var tables = allowedTables.Select(SqlPolicy.ParseTableIdentity).Distinct().ToArray();
        var predicates = new List<string>();
        foreach (var table in tables)
        {
            var index = predicates.Count;
            var schemaParameter = "@schema" + index;
            var tableParameter = "@table" + index;
            // Binary comparison matches the shared policy even on case-insensitive databases.
            predicates.Add($"(c.TABLE_SCHEMA COLLATE Latin1_General_100_BIN2 = {schemaParameter} " +
                $"AND DATALENGTH(c.TABLE_SCHEMA) = DATALENGTH({schemaParameter}) " +
                $"AND c.TABLE_NAME COLLATE Latin1_General_100_BIN2 = {tableParameter} " +
                $"AND DATALENGTH(c.TABLE_NAME) = DATALENGTH({tableParameter}))");
            AddParameter(command, schemaParameter, table.Schema!);
            AddParameter(command, tableParameter, table.Name);
        }
        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            MetadataSql, predicates.Count == 0 ? DenyAll : string.Join(" OR ", predicates));
        command.CommandTimeout = WorkspaceLimits.ConnectTimeoutSeconds;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Size = value.Length;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
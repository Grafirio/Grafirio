using System.Data.Common;

namespace Grafirio.QueryPolicy;

/// <summary>
/// Requires a dedicated least-privilege principal and complete database metadata visibility.
/// No permissions or database objects are changed. Missing visibility, unsupported permission
/// sets and verification errors block execution. DBA permission changes after verification
/// remain outside the application's guarantee; permissions must be managed operationally.
/// </summary>
public static class ReadOnlyPrincipalGuard
{
    private const int VerificationTimeoutSeconds = 30;
    private const string BlockedMessage =
        "SQL execution is blocked: a dedicated read-only principal with database VIEW DEFINITION " +
        "is required. Write, execute, ownership, impersonation and administrative permissions " +
        "must be removed by the database administrator. Verification must succeed; Grafirio does not modify permissions.";

    // Effective permissions include role inheritance; db_datareader alone proves nothing.
    private const string PrincipalVerificationSql = """
        SELECT CASE WHEN
            COALESCE(IS_SRVROLEMEMBER('sysadmin'), 1) = 0
            AND COALESCE(IS_ROLEMEMBER('db_owner'), 1) = 0
            AND COALESCE(IS_ROLEMEMBER('db_datawriter'), 1) = 0
            AND COALESCE(IS_ROLEMEMBER('db_ddladmin'), 1) = 0
            AND COALESCE(IS_ROLEMEMBER('db_securityadmin'), 1) = 0
            AND COALESCE(IS_ROLEMEMBER('db_accessadmin'), 1) = 0
            AND COALESCE(IS_ROLEMEMBER('db_backupoperator'), 1) = 0
            AND COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'), 0) = 1
            AND EXISTS (SELECT 1 FROM sys.fn_my_permissions(NULL, 'SERVER'))
            AND EXISTS (SELECT 1 FROM sys.fn_my_permissions(NULL, 'DATABASE'))
                        AND NOT EXISTS (
                                SELECT 1 FROM sys.login_token WHERE type = 'SERVER ROLE' AND name <> 'public')
                        AND NOT EXISTS (
                            SELECT 1 FROM sys.schemas s JOIN sys.user_token token ON token.principal_id = s.principal_id)
                        AND NOT EXISTS (
                            SELECT 1 FROM sys.objects o JOIN sys.user_token token ON token.principal_id = o.principal_id)
                        AND NOT EXISTS (
                            SELECT 1 FROM sys.database_principals p
                            JOIN sys.user_token token ON token.principal_id = p.owning_principal_id)
                        AND NOT EXISTS (
                            SELECT 1 FROM sys.assemblies a JOIN sys.user_token token ON token.principal_id = a.principal_id)
                        AND NOT EXISTS (
                            SELECT 1 FROM sys.types t JOIN sys.user_token token ON token.principal_id = t.principal_id)
                        AND NOT EXISTS (
                                SELECT 1 FROM sys.server_permissions p
                                JOIN sys.login_token token ON token.principal_id = p.grantee_principal_id
                                WHERE p.state IN ('G', 'W')
                                    AND (p.state = 'W' OR p.permission_name NOT IN ('CONNECT SQL', 'VIEW ANY DATABASE')))
                        AND NOT EXISTS (
                                SELECT 1 FROM sys.database_permissions p
                                JOIN sys.user_token token ON token.principal_id = p.grantee_principal_id
                                WHERE p.state IN ('G', 'W')
                                    AND (p.state = 'W' OR p.permission_name NOT IN ('CONNECT', 'SELECT', 'VIEW DEFINITION')))
            AND NOT EXISTS (
                SELECT 1 FROM sys.fn_my_permissions(NULL, 'SERVER')
                WHERE permission_name NOT IN ('CONNECT SQL', 'VIEW ANY DATABASE'))
            AND NOT EXISTS (
                SELECT 1 FROM sys.fn_my_permissions(NULL, 'DATABASE')
                WHERE permission_name NOT IN ('CONNECT', 'SELECT', 'VIEW DEFINITION'))
            AND NOT EXISTS (
                SELECT 1 FROM sys.schemas s
                CROSS APPLY sys.fn_my_permissions(QUOTENAME(s.name), 'SCHEMA') p
                WHERE p.permission_name NOT IN ('SELECT', 'VIEW DEFINITION'))
            AND NOT EXISTS (
                SELECT 1 FROM sys.objects o
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                CROSS APPLY sys.fn_my_permissions(QUOTENAME(s.name) + '.' + QUOTENAME(o.name), 'OBJECT') p
                WHERE o.is_ms_shipped = 0
                  AND p.permission_name NOT IN ('SELECT', 'VIEW DEFINITION'))
            AND NOT EXISTS (
                SELECT 1 FROM sys.columns c
                JOIN sys.objects o ON o.object_id = c.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE o.is_ms_shipped = 0 AND
                    (COALESCE(HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(o.name),
                        'OBJECT', 'UPDATE', c.name, 'COLUMN'), 1) <> 0
                     OR COALESCE(HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(o.name),
                        'OBJECT', 'REFERENCES', c.name, 'COLUMN'), 1) <> 0))
            THEN 1 ELSE 0 END
        """;

    // Views and synonyms hide sources; computed columns, CLR and RLS can invoke code.
    private const string PhysicalTableVerificationSql = """
        SELECT CASE WHEN EXISTS (
            SELECT 1 FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @Schema AND t.name = @Table
              AND t.is_ms_shipped = 0 AND t.is_external = 0
              AND NOT EXISTS (SELECT 1 FROM sys.computed_columns c WHERE c.object_id = t.object_id)
              AND NOT EXISTS (SELECT 1 FROM sys.security_predicates p WHERE p.target_object_id = t.object_id)
              AND NOT EXISTS (
                  SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                  WHERE c.object_id = t.object_id AND ty.is_assembly_type = 1)
        ) THEN 1 ELSE 0 END
        """;

    public static async Task VerifyAsync(DbConnection connection, CancellationToken ct = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PrincipalVerificationSql;
        command.CommandTimeout = VerificationTimeoutSeconds;
        try
        {
            if (await command.ExecuteScalarAsync(ct) is not int result || result != 1)
                throw new QueryPolicyException(BlockedMessage);
        }
        catch (DbException exception)
        {
            throw new InvalidOperationException(BlockedMessage, exception);
        }
    }

    public static async Task VerifyTablesAsync(
        DbConnection connection, QueryValidationResult validation, CancellationToken ct = default)
    {
        foreach (var table in validation.Tables.Where(table => !QueryPolicy.IsMetadata(table)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = PhysicalTableVerificationSql;
            command.CommandTimeout = VerificationTimeoutSeconds;
            AddParameter(command, "@Schema", table.Schema);
            AddParameter(command, "@Table", table.Name);
            if (await command.ExecuteScalarAsync(ct) is not int result || result != 1)
                throw new QueryPolicyException(
                    $"Table '{table.CanonicalName}' is blocked: only local base tables without computed columns, CLR types or security predicates are supported.", true);
        }
    }

    private static void AddParameter(DbCommand command, string name, string? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = (object?)value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
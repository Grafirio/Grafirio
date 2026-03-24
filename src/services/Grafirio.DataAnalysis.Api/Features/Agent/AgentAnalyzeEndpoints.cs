using System.Text.Json;
using Dapper;
using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Agent;

public static class AgentAnalyzeEndpoints
{
    public static void MapAgentAnalyzeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent")
            .WithTags("AI Agent")
            .WithOpenApi();

        group.MapPost("/analyze-connection/{connectionId:guid}", AnalyzeConnection)
            .WithName("AnalyzeConnection")
            .WithDescription("Kaydedilmiş bağlantının schema'sını Gemini ile analiz edip PyCaret config oluşturur");

        group.MapGet("/config/{connectionId:guid}", GetConfig)
            .WithName("GetAnalysisConfig")
            .WithDescription("Bağlantıya ait PyCaret config'ini getirir");

        group.MapGet("/config/{connectionId:guid}/status", GetConfigStatus)
            .WithName("GetConfigStatus")
            .WithDescription("Config oluşturma durumunu kontrol eder");
    }

    private static async Task<IResult> AnalyzeConnection(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] GeminiService gemini,
        [FromServices] ILogger<GeminiService> logger)
    {
        // 1. Kayıtlı bağlantıyı bul
        var savedConn = await db.SavedConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.IsActive);

        if (savedConn is null)
            return Results.NotFound(new { error = "Bağlantı bulunamadı" });

        // Varsa eski config'i kontrol et
        var existingConfig = await db.AnalysisConfigs
            .FirstOrDefaultAsync(c => c.ConnectionId == connectionId && c.IsActive && c.Status == "ready");

        if (existingConfig is not null)
        {
            return Results.Ok(new
            {
                success = true,
                message = "Config zaten mevcut",
                configId = existingConfig.Id,
                config = JsonSerializer.Deserialize<JsonElement>(existingConfig.ConfigJson),
                schemaSummary = existingConfig.SchemaSummary,
                status = existingConfig.Status
            });
        }

        // 2. Pending config oluştur
        var config = new AnalysisConfig
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            UserId = savedConn.UserId,
            CompanyId = savedConn.CompanyId,
            DatabaseName = savedConn.Database,
            Status = "analyzing",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        db.AnalysisConfigs.Add(config);
        await db.SaveChangesAsync();

        // 3. SQL Server'dan schema bilgisini çek
        SchemaInfo schemaInfo;
        try
        {
            var password = EncryptionHelper.Decrypt(savedConn.EncryptedPassword);
            var connStr = BuildConnectionString(savedConn, password);
            schemaInfo = await FetchSchemaFromDatabase(connStr, savedConn.Database);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Schema bilgisi çekilemedi");
            config.Status = "failed";
            config.SchemaSummary = $"Schema çekme hatası: {ex.Message}";
            await db.SaveChangesAsync();
            return Results.Problem($"Veritabanı schema'sı çekilemedi: {ex.Message}");
        }

        // 4. Gemini'ye gönder
        var result = await gemini.AnalyzeSchemaForPyCaret(schemaInfo);

        if (!result.Success)
        {
            config.Status = "failed";
            config.SchemaSummary = $"LLM hatası: {result.Error}";
            await db.SaveChangesAsync();
            return Results.Problem($"LLM analizi başarısız: {result.Error}");
        }

        // 5. Config'i kaydet
        config.ConfigJson = result.ConfigJson;
        config.SchemaSummary = result.SchemaSummary;
        config.TablesJson = JsonSerializer.Serialize(schemaInfo.Tables.Select(t => t.TableName));
        config.Status = "ready";
        config.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Config oluşturuldu: {ConfigId} for connection {ConnectionId}", config.Id, connectionId);

        return Results.Ok(new
        {
            success = true,
            message = "Schema analiz edildi ve PyCaret config oluşturuldu",
            configId = config.Id,
            config = JsonSerializer.Deserialize<JsonElement>(result.ConfigJson),
            schemaSummary = result.SchemaSummary,
            status = "ready"
        });
    }

    private static async Task<IResult> GetConfig(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db)
    {
        var config = await db.AnalysisConfigs
            .Where(c => c.ConnectionId == connectionId && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (config is null)
            return Results.NotFound(new { error = "Bu bağlantı için henüz analiz yapılmamış" });

        return Results.Ok(new
        {
            success = true,
            configId = config.Id,
            connectionId = config.ConnectionId,
            databaseName = config.DatabaseName,
            config = config.Status == "ready"
                ? JsonSerializer.Deserialize<JsonElement>(config.ConfigJson)
                : (JsonElement?)null,
            schemaSummary = config.SchemaSummary,
            tables = JsonSerializer.Deserialize<JsonElement>(config.TablesJson),
            status = config.Status,
            createdAt = config.CreatedAt,
            updatedAt = config.UpdatedAt
        });
    }

    private static async Task<IResult> GetConfigStatus(
        Guid connectionId,
        [FromServices] DataAnalysisDbContext db)
    {
        var config = await db.AnalysisConfigs
            .Where(c => c.ConnectionId == connectionId && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Status, c.CreatedAt })
            .FirstOrDefaultAsync();

        if (config is null)
            return Results.Ok(new { exists = false, status = "none" });

        return Results.Ok(new { exists = true, configId = config.Id, status = config.Status, createdAt = config.CreatedAt });
    }

    // --- Helpers ---

    private static string BuildConnectionString(SavedConnection conn, string password)
    {
        var csb = new SqlConnectionStringBuilder
        {
            DataSource = conn.Port != 1433 ? $"{conn.Host},{conn.Port}" : conn.Host,
            InitialCatalog = conn.Database,
            UserID = conn.Username,
            Password = password,
            TrustServerCertificate = conn.TrustServerCertificate,
            ConnectTimeout = 15
        };
        return csb.ConnectionString;
    }

    private static async Task<SchemaInfo> FetchSchemaFromDatabase(string connectionString, string dbName)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Tabloları al
        var tablesQuery = @"
            SELECT TABLE_NAME as TableName, TABLE_SCHEMA as [Schema]
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME";

        var tables = (await connection.QueryAsync<dynamic>(tablesQuery)).ToList();

        var schemaInfo = new SchemaInfo { DatabaseName = dbName };

        foreach (var table in tables)
        {
            string tableName = table.TableName;
            string schema = table.Schema;

            // Kolon bilgilerini al
            var columnsQuery = @"
                SELECT COLUMN_NAME as ColumnName, DATA_TYPE as DataType,
                       CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END as IsNullable,
                       CHARACTER_MAXIMUM_LENGTH as MaxLength
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table
                ORDER BY ORDINAL_POSITION";

            var columns = (await connection.QueryAsync<ColumnDetail>(
                columnsQuery, new { Schema = schema, Table = tableName })).ToList();

            // Satır sayısı
            int rowCount = 0;
            try
            {
                rowCount = await connection.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM [{schema}].[{tableName}]");
            }
            catch { /* skip permission errors */ }

            schemaInfo.Tables.Add(new TableSchemaDetail
            {
                TableName = tableName,
                Schema = schema,
                RowCount = rowCount,
                Columns = columns
            });
        }

        return schemaInfo;
    }
}

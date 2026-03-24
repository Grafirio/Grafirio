using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

public static class SavedConnectionEndpoints
{
    public static void MapSavedConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connections");

        group.MapPost("/", SaveConnection)
            .WithName("SaveConnection")
            .WithTags("Saved Connections");

        group.MapGet("/", GetConnections)
            .WithName("GetConnections")
            .WithTags("Saved Connections");

        group.MapGet("/{id:guid}", GetConnectionById)
            .WithName("GetConnectionById")
            .WithTags("Saved Connections");

        group.MapPut("/{id:guid}", UpdateConnection)
            .WithName("UpdateConnection")
            .WithTags("Saved Connections");

        group.MapDelete("/{id:guid}", DeleteConnection)
            .WithName("DeleteConnection")
            .WithTags("Saved Connections");
            
        group.MapGet("/{id:guid}/decrypt", GetDecryptedConnection)
            .WithName("GetDecryptedConnection")
            .WithTags("Saved Connections");
    }

    private static async Task<IResult> SaveConnection(
        [FromBody] SaveConnectionRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            logger.LogInformation("Saving connection for UserId: {UserId}, Name: {Name}, CompanyId: {CompanyId}", 
                request.UserId, request.Name, request.CompanyId);
            
            logger.LogInformation("Connection details - Host: {Host}, Port: {Port}, Database: {Database}, Username: {Username}, TrustServerCertificate: {Trust}",
                request.Host, request.Port, request.Database, request.Username, request.TrustServerCertificate);

            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                logger.LogWarning("SaveConnection failed: UserId is null or empty");
                return Results.BadRequest(new { error = "UserId is required" });
            }
            
            if (string.IsNullOrWhiteSpace(request.CompanyId))
            {
                logger.LogWarning("SaveConnection failed: CompanyId is null or empty");
                return Results.BadRequest(new { error = "CompanyId is required" });
            }
            
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                logger.LogWarning("SaveConnection failed: Name is null or empty");
                return Results.BadRequest(new { error = "Name is required" });
            }

            // Check if connection name already exists for user
            var existingConnection = await db.SavedConnections
                .FirstOrDefaultAsync(c => c.UserId == request.UserId && c.Name == request.Name && c.IsActive);

            if (existingConnection != null)
            {
                // Aynı isimde connection varsa güncelle
                logger.LogInformation("Connection already exists, updating it. Id: {Id}", existingConnection.Id);
                
                existingConnection.Host = request.Host;
                existingConnection.Port = request.Port;
                existingConnection.Database = request.Database;
                existingConnection.Username = request.Username;
                existingConnection.EncryptedPassword = EncryptionHelper.Encrypt(request.Password);
                existingConnection.TrustServerCertificate = request.TrustServerCertificate;
                existingConnection.UpdatedAt = DateTime.UtcNow;
                
                await db.SaveChangesAsync();
                
                return Results.Ok(new
                {
                    success = true,
                    message = "Bağlantı güncellendi",
                    connectionId = existingConnection.Id
                });
            }

            // Encrypt password
            var encryptedPassword = EncryptionHelper.Encrypt(request.Password);

            var connection = new SavedConnection
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                CompanyId = request.CompanyId,
                Name = request.Name,
                Host = request.Host,
                Port = request.Port,
                Database = request.Database,
                Username = request.Username,
                EncryptedPassword = encryptedPassword,
                TrustServerCertificate = request.TrustServerCertificate,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            db.SavedConnections.Add(connection);
            await db.SaveChangesAsync();

            logger.LogInformation("Connection saved successfully: {ConnectionId}", connection.Id);

            return Results.Ok(new
            {
                success = true,
                message = "Bağlantı kaydedildi",
                connectionId = connection.Id
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving connection");
            return Results.Problem("Bağlantı kaydedilemedi: " + ex.Message);
        }
    }

    private static async Task<IResult> GetConnections(
        [FromQuery] string userId,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
            {
                return Results.BadRequest(new { error = "UserId gerekli" });
            }

            var connections = await db.SavedConnections
                .Where(c => c.UserId == userId && c.IsActive)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    host = c.Host,
                    port = c.Port,
                    database = c.Database,
                    username = c.Username,
                    trustServerCertificate = c.TrustServerCertificate,
                    createdAt = c.CreatedAt,
                    updatedAt = c.UpdatedAt,
                    lastConnectedAt = c.LastConnectedAt
                })
                .ToListAsync();

            return Results.Ok(new { success = true, connections });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting connections for UserId: {UserId}", userId);
            return Results.Problem("Bağlantılar getirilemedi: " + ex.Message);
        }
    }

    private static async Task<IResult> GetConnectionById(
        Guid id,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            var connection = await db.SavedConnections
                .Where(c => c.Id == id && c.IsActive)
                .Select(c => new
                {
                    id = c.Id,
                    userId = c.UserId,
                    companyId = c.CompanyId,
                    name = c.Name,
                    host = c.Host,
                    port = c.Port,
                    database = c.Database,
                    username = c.Username,
                    trustServerCertificate = c.TrustServerCertificate,
                    createdAt = c.CreatedAt,
                    lastConnectedAt = c.LastConnectedAt
                })
                .FirstOrDefaultAsync();

            if (connection == null)
            {
                return Results.NotFound(new { error = "Bağlantı bulunamadı" });
            }

            return Results.Ok(connection);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting connection: {ConnectionId}", id);
            return Results.Problem("Bağlantı getirilemedi: " + ex.Message);
        }
    }
    
    private static async Task<IResult> GetDecryptedConnection(
        Guid id,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            var connection = await db.SavedConnections
                .FirstOrDefaultAsync(c => c.Id == id && c.IsActive);

            if (connection == null)
            {
                return Results.NotFound(new { error = "Bağlantı bulunamadı" });
            }

            // Decrypt password
            var decryptedPassword = EncryptionHelper.Decrypt(connection.EncryptedPassword);

            // Update last connected time
            connection.LastConnectedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                success = true,
                connection = new
                {
                    id = connection.Id,
                    userId = connection.UserId,
                    companyId = connection.CompanyId,
                    name = connection.Name,
                    host = connection.Host,
                    port = connection.Port,
                    database = connection.Database,
                    username = connection.Username,
                    password = decryptedPassword,
                    trustServerCertificate = connection.TrustServerCertificate
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error decrypting connection: {ConnectionId}", id);
            return Results.Problem("Bağlantı şifresi çözülemedi: " + ex.Message);
        }
    }

    private static async Task<IResult> UpdateConnection(
        Guid id,
        [FromBody] UpdateConnectionRequest request,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            var connection = await db.SavedConnections
                .FirstOrDefaultAsync(c => c.Id == id && c.IsActive);

            if (connection == null)
            {
                return Results.NotFound(new { error = "Bağlantı bulunamadı" });
            }

            // Update fields
            connection.Name = request.Name ?? connection.Name;
            connection.Host = request.Host ?? connection.Host;
            connection.Port = request.Port ?? connection.Port;
            connection.Database = request.Database ?? connection.Database;
            connection.Username = request.Username ?? connection.Username;
            
            if (!string.IsNullOrEmpty(request.Password))
            {
                connection.EncryptedPassword = EncryptionHelper.Encrypt(request.Password);
            }
            
            connection.TrustServerCertificate = request.TrustServerCertificate ?? connection.TrustServerCertificate;
            connection.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            logger.LogInformation("Connection updated: {ConnectionId}", id);

            return Results.Ok(new { success = true, message = "Bağlantı güncellendi" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating connection: {ConnectionId}", id);
            return Results.Problem("Bağlantı güncellenemedi: " + ex.Message);
        }
    }

    private static async Task<IResult> DeleteConnection(
        Guid id,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            var connection = await db.SavedConnections
                .FirstOrDefaultAsync(c => c.Id == id && c.IsActive);

            if (connection == null)
            {
                return Results.NotFound(new { error = "Bağlantı bulunamadı" });
            }

            // Soft delete
            connection.IsActive = false;
            connection.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            logger.LogInformation("Connection deleted: {ConnectionId}", id);

            return Results.Ok(new { success = true, message = "Bağlantı silindi" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting connection: {ConnectionId}", id);
            return Results.Problem("Bağlantı silinemedi: " + ex.Message);
        }
    }
}

public record SaveConnectionRequest(
    string UserId,
    string CompanyId,
    string Name,
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate
);

public record UpdateConnectionRequest(
    string? Name,
    string? Host,
    int? Port,
    string? Database,
    string? Username,
    string? Password,
    bool? TrustServerCertificate
);

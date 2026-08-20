using Grafirio.DataAnalysis.Api.Data;
using Grafirio.DataAnalysis.Api.Data.Entities;
using Grafirio.DataAnalysis.Api.Features.Bridge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Grafirio.Shared.Identity.Extensions;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;

namespace Grafirio.DataAnalysis.Api.Features.Connections;

public static class SavedConnectionEndpoints
{
    public static void MapSavedConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        // Tum grup yetki istiyor. Onceden hicbiri istemiyordu ve kullanici
        // kimligi sorgu dizesinden geliyordu; ?userId=<baskasi> yazan herkes
        // o kisinin kayitli baglantilarini, /decrypt ile de veritabani
        // parolasini okuyabiliyordu.
        // Politika adi bilerek veriliyor. Ciplak RequireAuthorization() burada
        // calismiyordu: paylasilan kurulum AddAuthentication()'i varsayilan sema
        // vermeden cagiriyor, dolayisiyla "No authenticationScheme was specified"
        // ile her istek 400 donuyordu — bu uc uretimde bu yuzden bozuktu.
        // CompanyAccess hem semayi belirtiyor hem de company_id claim'ini sart
        // kosuyor; zaten sirket suzgeci icin ona ihtiyacimiz var.
        var group = app.MapGroup("/api/connections").RequireAuthorization("CompanyAccess");

        // Sirkete erisim yetmiyor: departman "veri kaynaklarini gorsun ama
        // degistirmesin" diyebiliyor ve bu ayrimin uygulandigi yer burasi.
        // Izinler Identity'den soruluyor (bkz. AddGrafirioPermissions).
        group.MapPost("/", SaveConnection)
            .RequirePermission(AppPermissions.DataSourcesCreate)
            .WithName("SaveConnection")
            .WithTags("Saved Connections");

        group.MapGet("/", GetConnections)
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithName("GetConnections")
            .WithTags("Saved Connections");

        group.MapGet("/{id:guid}", GetConnectionById)
            .RequirePermission(AppPermissions.DataSourcesRead)
            .WithName("GetConnectionById")
            .WithTags("Saved Connections");

        group.MapPut("/{id:guid}", UpdateConnection)
            .RequirePermission(AppPermissions.DataSourcesUpdate)
            .WithName("UpdateConnection")
            .WithTags("Saved Connections");

        group.MapDelete("/{id:guid}", DeleteConnection)
            .RequirePermission(AppPermissions.DataSourcesDelete)
            .WithName("DeleteConnection")
            .WithTags("Saved Connections");

        // Cozulmus parola okumak READ degil UPDATE: baglanti bilgisini
        // gormek ile veritabani parolasini almak ayni agirlikta isler degil,
        // ve bu ucun tek kullanim yeri baglantiyi duzenleme formu.
        group.MapGet("/{id:guid}/decrypt", GetDecryptedConnection)
            .RequirePermission(AppPermissions.DataSourcesUpdate)
            .WithName("GetDecryptedConnection")
            .WithTags("Saved Connections");
    }

    private static async Task<IResult> SaveConnection(
        [FromBody] SaveConnectionRequest request,
        [FromServices] IIdentityService identity,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] BridgeConnectionSync bridgeSync,
        [FromServices] BridgeRegistry bridgeRegistry,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            // Kimlik istekten degil token'dan. Istemcinin gonderdigi UserId ve
            // CompanyId'ye guvenmek, baskasinin firmasina kayit yazmayi
            // mumkun kiliyordu.
            var callerId = identity.UserId.ToString();
            var callerCompanyId = identity.CurrentCompanyId;

            if (callerCompanyId is null)
            {
                return Results.BadRequest(new { error = "Hesabınız bir firmaya bağlı değil" });
            }

            logger.LogInformation("Saving connection for UserId: {UserId}, Name: {Name}, CompanyId: {CompanyId}",
                callerId, request.Name, callerCompanyId);
            
            logger.LogInformation("Connection details - Host: {Host}, Port: {Port}, Database: {Database}, Username: {Username}, TrustServerCertificate: {Trust}",
                request.Host, request.Port, request.Database, request.Username, request.TrustServerCertificate);

            // UserId ve CompanyId artik istekten okunmuyor, dogrulanmasi da
            // gerekmiyor; ikisi de token'dan geliyor.
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                logger.LogWarning("SaveConnection failed: Name is null or empty");
                return Results.BadRequest(new { error = "Name is required" });
            }

            // Check if connection name already exists for user
            var existingConnection = await db.SavedConnections
                .FirstOrDefaultAsync(c => c.CompanyId == callerCompanyId.Value.ToString() && c.Name == request.Name && c.IsActive);

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

                await PushToBridgeAsync(
                    bridgeSync, bridgeRegistry, logger, existingConnection.Id, existingConnection.CompanyId);

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
                UserId = callerId,
                CompanyId = callerCompanyId.Value.ToString(),
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

            await PushToBridgeAsync(
                bridgeSync, bridgeRegistry, logger, connection.Id, connection.CompanyId);

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

    /// <summary>
    /// Baglanti tanimini sirketin cevrimici bridge'ine iletir.
    ///
    /// Bu adim ZORUNLU: bridge yalnizca kendi deposunda tanimli baglantilara
    /// sorgu calistiriyor. Gonderilmezse yeni kaydedilen baglanti, bridge bir
    /// sonraki yeniden baglanmasina kadar "bu baglanti bridge uzerinde tanimli
    /// degil" ile duser — kullanicinin anlayamayacagi bir hata.
    ///
    /// Hata yutuluyor ama loglaniyor: tanim gonderilemedi diye KAYIT
    /// basarisiz sayilmamali. Baglanti Postgres'te duruyor ve bridge
    /// baglandiginda hepsi yeniden gonderiliyor; yani bu, kendi kendini
    /// onaran bir eksiklik.
    /// </summary>
    private static async Task PushToBridgeAsync(
        BridgeConnectionSync bridgeSync,
        BridgeRegistry bridgeRegistry,
        ILogger logger,
        Guid connectionId,
        string companyId)
    {
        try
        {
            await bridgeSync.SyncOneAsync(connectionId, companyId, bridgeRegistry);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Bağlantı tanımı bridge'e gönderilemedi: {ConnectionId}. " +
                "Bridge bağlandığında yeniden gönderilecek.", connectionId);
        }
    }

    private static async Task<IResult> GetConnections(
        [FromServices] IIdentityService identity,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            var companyId = identity.CurrentCompanyId;

            if (companyId is null)
            {
                return Results.BadRequest(new { error = "Hesabınız bir firmaya bağlı değil" });
            }

            // Baglanti firmanin ortak varligi: ayni firmadaki herkes gorur.
            // Onceden yalnizca UserId ile suzuluyordu, bu yuzden kayit kisiyle
            // birlikte firmadan firmaya tasiniyor ve firma degistiginde eski
            // baglanti yeni calisma alaninda gorunuyordu.
            var connections = await db.SavedConnections
                .Where(c => c.CompanyId == companyId.Value.ToString() && c.IsActive)
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
            logger.LogError(ex, "Error getting connections");
            return Results.Problem("Bağlantılar getirilemedi: " + ex.Message);
        }
    }

    private static async Task<IResult> GetConnectionById(
        Guid id,
        [FromServices] IIdentityService identity,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            // Kayit id ile isteniyor ama id tahmin edilebilir bir sey degilse
            // bile baska firmanin kaydina denk gelebilir; firma kontrolu
            // sorgunun kendisinde.
            var scopedCompany = identity.CurrentCompanyId;
            if (scopedCompany is null)
            {
                return Results.BadRequest(new { error = "Hesabınız bir firmaya bağlı değil" });
            }
            var scopedCompanyId = scopedCompany.Value.ToString();

            var connection = await db.SavedConnections
                .Where(c => c.Id == id && c.CompanyId == scopedCompanyId && c.IsActive)
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
        [FromServices] IIdentityService identity,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            // Kayit id ile isteniyor ama id tahmin edilebilir bir sey degilse
            // bile baska firmanin kaydina denk gelebilir; firma kontrolu
            // sorgunun kendisinde.
            var scopedCompany = identity.CurrentCompanyId;
            if (scopedCompany is null)
            {
                return Results.BadRequest(new { error = "Hesabınız bir firmaya bağlı değil" });
            }
            var scopedCompanyId = scopedCompany.Value.ToString();

            var connection = await db.SavedConnections
                .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == scopedCompanyId && c.IsActive);

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
        [FromServices] IIdentityService identity,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] BridgeConnectionSync bridgeSync,
        [FromServices] BridgeRegistry bridgeRegistry,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            // Kayit id ile isteniyor ama id tahmin edilebilir bir sey degilse
            // bile baska firmanin kaydina denk gelebilir; firma kontrolu
            // sorgunun kendisinde.
            var scopedCompany = identity.CurrentCompanyId;
            if (scopedCompany is null)
            {
                return Results.BadRequest(new { error = "Hesabınız bir firmaya bağlı değil" });
            }
            var scopedCompanyId = scopedCompany.Value.ToString();

            var connection = await db.SavedConnections
                .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == scopedCompanyId && c.IsActive);

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

            // Degisen tanim bridge'e de gitmeli: sifre ya da host degistiginde
            // bridge eski bilgiyle sorgu calistirmaya devam ederdi.
            await PushToBridgeAsync(
                bridgeSync, bridgeRegistry, logger, connection.Id, connection.CompanyId);

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
        [FromServices] IIdentityService identity,
        [FromServices] DataAnalysisDbContext db,
        [FromServices] BridgeConnectionSync bridgeSync,
        [FromServices] BridgeRegistry bridgeRegistry,
        [FromServices] ILogger<SaveConnectionRequest> logger)
    {
        try
        {
            // Kayit id ile isteniyor ama id tahmin edilebilir bir sey degilse
            // bile baska firmanin kaydina denk gelebilir; firma kontrolu
            // sorgunun kendisinde.
            var scopedCompany = identity.CurrentCompanyId;
            if (scopedCompany is null)
            {
                return Results.BadRequest(new { error = "Hesabınız bir firmaya bağlı değil" });
            }
            var scopedCompanyId = scopedCompany.Value.ToString();

            var connection = await db.SavedConnections
                .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == scopedCompanyId && c.IsActive);

            if (connection == null)
            {
                return Results.NotFound(new { error = "Bağlantı bulunamadı" });
            }

            // Soft delete
            connection.IsActive = false;
            connection.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            // Silinen baglantiyi bridge de unutmali. Aksi halde tanim (ve
            // sifre) musterinin diskinde kalmaya devam ederdi.
            try
            {
                await bridgeSync.ForgetAsync(id, connection.CompanyId, bridgeRegistry);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Silinen bağlantı bridge'e bildirilemedi: {ConnectionId}", id);
            }

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

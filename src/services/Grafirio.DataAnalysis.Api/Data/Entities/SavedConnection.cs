namespace Grafirio.DataAnalysis.Api.Data.Entities;

/// <summary>
/// Kaydedilmiş SQL Server bağlantı bilgileri
/// </summary>
public class SavedConnection
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Kullanıcı ID (Keycloak userId)
    /// </summary>
    public string UserId { get; set; } = string.Empty;
    
    /// <summary>
    /// Şirket/Company ID
    /// </summary>
    public string CompanyId { get; set; } = string.Empty;
    
    /// <summary>
    /// Bağlantı adı (kullanıcı tanımlı)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Database host adresi
    /// </summary>
    public string Host { get; set; } = string.Empty;
    
    /// <summary>
    /// Port numarası
    /// </summary>
    public int Port { get; set; }
    
    /// <summary>
    /// Database adı
    /// </summary>
    public string Database { get; set; } = string.Empty;
    
    /// <summary>
    /// SQL username
    /// </summary>
    public string Username { get; set; } = string.Empty;
    
    /// <summary>
    /// Şifrelenmiş password (AES256)
    /// </summary>
    public string EncryptedPassword { get; set; } = string.Empty;
    
    /// <summary>
    /// Trust server certificate
    /// </summary>
    public bool TrustServerCertificate { get; set; } = true;
    
    /// <summary>
    /// Bağlantı oluşturulma tarihi
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Son güncelleme tarihi
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    
    /// <summary>
    /// Son başarılı bağlantı tarihi
    /// </summary>
    public DateTime? LastConnectedAt { get; set; }
    
    /// <summary>
    /// Aktif/pasif durumu
    /// </summary>
    public bool IsActive { get; set; } = true;
}

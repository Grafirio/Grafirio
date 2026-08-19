using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Users;

public class UserCompanyRole : BaseEntity
{
    public string KeycloakUserId { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string? AssignedBy { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
}

public static class CompanyRoles
{
    public const string COMPANY_ADMIN = "COMPANY_ADMIN";
    public const string COMPANY_MANAGER = "COMPANY_MANAGER";
    public const string COMPANY_USER = "COMPANY_USER";

    public static readonly string[] All = [COMPANY_ADMIN, COMPANY_MANAGER, COMPANY_USER];

    public static bool IsValid(string role) => All.Contains(role);
}

/// <summary>
/// Firma kapsamının dışındaki roller. <see cref="CompanyRoles"/> bir kullanıcının
/// tek bir firma içindeki yetkisini anlatır; buradaki rol ise platformu işleten
/// ekibi tanımlar ve tüm firmalar üzerinde geçerlidir.
/// </summary>
public static class PlatformRoles
{
    /// <summary>
    /// Grafirio personeli: müşteri firmalarının tamamını görür ve yönetir.
    /// ProjectAdmin bu rolle korunur.
    /// </summary>
    public const string PLATFORM_ADMIN = "PLATFORM_ADMIN";
}

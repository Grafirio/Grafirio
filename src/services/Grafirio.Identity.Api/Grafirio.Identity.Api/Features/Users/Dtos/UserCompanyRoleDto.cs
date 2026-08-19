namespace Grafirio.Identity.Api.Features.Users.Dtos;

public class UserCompanyRoleDto
{
    public Guid Id { get; set; }
    public string KeycloakUserId { get; set; } = string.Empty;

    /// <summary>
    /// Panelde gösterilecek ad — Keycloak'tan okunuyor, yetki kaydında
    /// saklanmıyor (bkz. <see cref="Directory.KeycloakUserDirectory"/>).
    /// Kullanıcı Keycloak'ta bulunamazsa (silinmiş olabilir, kayıt denetim izi
    /// olarak duruyor) null kalır ve panel kimliği gösterir.
    /// </summary>
    public string? DisplayName { get; set; }

    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public Guid CompanyId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime AssignedAt { get; set; }
    public string? AssignedBy { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
}

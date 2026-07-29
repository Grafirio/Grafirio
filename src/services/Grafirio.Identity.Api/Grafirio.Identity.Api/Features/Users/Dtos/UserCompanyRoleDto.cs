namespace Grafirio.Identity.Api.Features.Users.Dtos;

public class UserCompanyRoleDto
{
    public Guid Id { get; set; }
    public string KeycloakUserId { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime AssignedAt { get; set; }
    public string? AssignedBy { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
}

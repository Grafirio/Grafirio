namespace Grafirio.Identity.Api.Features.Departments.Dtos;

public class DepartmentDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? ManagerKeycloakUserId { get; set; }
    public string? CostCenter { get; set; }

    /// Bu departmandaki kullanıcıların girebileceği modüller. Rol tavanını
    /// daraltır, genişletemez.
    public List<string> Modules { get; set; } = [];

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// Aktif üye sayısı. Listede her departman için ayrı istek atmak yerine
    /// tek sorguda hesaplanıp buraya yazılıyor.
    public int MemberCount { get; set; }
}

public class DepartmentMemberDto
{
    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public string KeycloakUserId { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
    public string? AssignedBy { get; set; }
}

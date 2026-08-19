namespace Grafirio.Identity.Api.Features.Roles.Dtos;

public class RoleDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// Rolün izinleri (MODÜL.AKSİYON). Kullanıcının etkin izni, taşıdığı
    /// rollerin ve kişisel izinlerinin birleşimi.
    public List<string> Permissions { get; set; } = [];

    /// İzinlerin dokunduğu modüller — menü çizimi için, izinlerden türetiliyor.
    public List<string> Modules { get; set; } = [];

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// Bu rolü taşıyan aktif kullanıcı sayısı. Listede her rol için ayrı istek
    /// atmak yerine tek sorguda hesaplanıp buraya yazılıyor.
    public int MemberCount { get; set; }
}

public class RoleMemberDto
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public string KeycloakUserId { get; set; } = string.Empty;

    /// Keycloak'tan okunan görünen ad; bulunamazsa null (panel kimliği gösterir).
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTime AssignedAt { get; set; }
    public string? AssignedBy { get; set; }
}

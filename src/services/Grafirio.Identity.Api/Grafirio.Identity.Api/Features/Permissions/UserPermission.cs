using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Permissions;

/// <summary>
/// Kişiye doğrudan verilen izinler — rollerinin dışında kalanlar.
///
/// Rolle ifade edilemeyen tek kişilik durumlar için: biri hem Muhasebe
/// rolünde olup hem o rolde bulunmayan bir izni taşıyabilsin. Rol tanımını
/// tek bir kişi için bozmak yerine burası kullanılıyor.
///
/// Şirket başına tek kayıt: aynı kişi için iki ayrı kişisel izin listesi
/// tutmak, hangisinin geçerli olduğunu belirsiz bırakırdı.
/// </summary>
public class UserPermission : BaseEntity
{
    public string KeycloakUserId { get; set; } = string.Empty;

    public Guid CompanyId { get; set; }

    public List<string> Permissions { get; set; } = [];

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}

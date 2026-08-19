using Grafirio.Identity.Api.Repositories;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Roles;

/// <summary>
/// Şirketin kendi tanımladığı, adlandırılmış bir izin kümesi — "Muhasebe",
/// "Saha", "Raporlama".
///
/// Şirkete bağlı, ağaca değil: alt şirketler ayrı tüzel kişilik ve kendi
/// izin şemalarını taşıyorlar. Bu sayede aynı kişi bir şirkette muhasebeci,
/// diğerinde finansçı olabiliyor — izin verilirken önce şirket seçiliyor.
///
/// Rol <b>izin verir</b>, daraltmaz. Önceki modelde departman bir rol
/// tavanının kesişimiydi ve bu yüzden "muhasebe müdürü kullanıcı yönetsin"
/// denemiyordu: tavanda yoksa departman ekleyemiyordu. Tavan kavramı kalktı.
/// </summary>
public class Role : BaseEntity
{
    public Guid CompanyId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// İzin anahtarları (<see cref="AppPermissions"/>, <c>MODÜL.AKSİYON</c>).
    ///
    /// Boş liste "izin yok" demek. Kurucu ve admin bu listeye hiç bakmıyor;
    /// onlar izin şemasının dışında.
    /// </summary>
    public List<string> Permissions { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Kullanıcının bir role ataması.
///
/// Bir kişi aynı şirkette birden fazla rol taşıyabilir; etkin izin bunların
/// ve kişisel izinlerinin birleşimi.
///
/// Kayıt silinmiyor, kapatılıyor: kimin ne zaman hangi rolde olduğu sonradan
/// sorulabilsin.
/// </summary>
public class UserRole : BaseEntity
{
    public string KeycloakUserId { get; set; } = string.Empty;

    public Guid RoleId { get; set; }

    /// Rolün şirketi. Rol kaydına gitmeden şirket kapsamlı sorgu yazılabilsin
    /// diye burada da tutuluyor.
    public Guid CompanyId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string? AssignedBy { get; set; }
    public DateTime? RemovedAt { get; set; }
    public string? RemovedBy { get; set; }
}

using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Departments;

/// <summary>
/// Kullanıcının bir departmana üyeliği. Bir kişi birden fazla departmanda
/// olabilir; ileride izinler eklendiğinde etkin izin bunların birleşimi
/// olacak.
///
/// Kayıt silinmiyor, kapatılıyor: <see cref="Users.UserCompanyRole"/> ile aynı
/// denetim izi deseni — kimin ne zaman hangi departmanda olduğu sonradan
/// sorulabilsin.
/// </summary>
public class UserDepartment : BaseEntity
{
    public string KeycloakUserId { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }

    /// Departmanın şirketi. Departman kaydına gitmeden şirket kapsamlı
    /// sorgular yazılabilsin diye burada da tutuluyor.
    public Guid CompanyId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string? AssignedBy { get; set; }
    public DateTime? RemovedAt { get; set; }
    public string? RemovedBy { get; set; }
}

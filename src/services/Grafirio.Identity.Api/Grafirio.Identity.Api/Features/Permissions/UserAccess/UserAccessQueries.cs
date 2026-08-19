namespace Grafirio.Identity.Api.Features.Permissions.UserAccess;

/// <summary>
/// Bir kullanıcının bir şirketteki yetkisinin tamamı: üyelik seviyesi,
/// taşıdığı roller, kişisel izinleri ve bunların birleşimi.
///
/// Panelde "bu kişi neden bunu yapabiliyor" sorusunu cevaplamak için tek
/// parça geliyor: çok rollü birinde hangi iznin nereden geldiği, üç ayrı
/// istekten birleştirilirse ekranda tutarsız görünebilir.
/// </summary>
public record GetUserAccessQuery(Guid CompanyId, string KeycloakUserId)
    : IRequestByServiceResult<UserAccessDto>;

public class UserAccessDto
{
    public Guid CompanyId { get; set; }
    public string KeycloakUserId { get; set; } = string.Empty;

    /// <see cref="Users.MembershipLevels"/>; üyeliği yoksa null.
    public string? Level { get; set; }

    /// <summary>
    /// Kurucu ve admin izin şemasının dışında: rolleri ve kişisel izinleri
    /// yok sayılır, her şeye erişirler. Panel bu durumda matrisi düzenlenemez
    /// gösteriyor.
    /// </summary>
    public bool BypassesPermissions { get; set; }

    public List<UserAccessRoleDto> Roles { get; set; } = [];

    /// Rollerin dışında, kişiye doğrudan verilmiş izinler.
    public List<string> PersonalPermissions { get; set; } = [];

    /// Rollerin ve kişisel izinlerin birleşimi — fiilen uygulanan küme.
    public List<string> EffectivePermissions { get; set; } = [];
}

public record UserAccessRoleDto(Guid Id, string Name, List<string> Permissions);

/// <summary>
/// Kişisel izinleri değiştirir. Liste tam olarak gönderiliyor: eksik gelen bir
/// anahtar kaldırılmış sayılıyor, aksi halde izin kaldırmanın ayrı bir ucu
/// gerekirdi.
/// </summary>
public record SetUserPermissionsCommand(
    Guid CompanyId,
    string KeycloakUserId,
    List<string>? Permissions
) : IRequestByServiceResult<bool>;

/// Gövde tipi: uç, kimliği yoldan alıyor, gövdede yalnızca izin listesi var.
public record SetUserPermissionsRequest(List<string>? Permissions);

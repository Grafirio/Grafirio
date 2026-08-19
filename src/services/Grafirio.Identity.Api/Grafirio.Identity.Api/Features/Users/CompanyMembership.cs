using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Users;

/// <summary>
/// Kişinin bir şirketteki üyeliği.
///
/// Üyelik ile yetki ayrı: bu kayıt "bu kişi bu şirkette var mı" sorusunu ve
/// yalnızca üç durumlu bir ayrımı taşıyor. Ne yapabildiği ise izin
/// kümelerinden geliyor (bkz. <see cref="Permissions.Role"/>,
/// <see cref="Permissions.UserPermission"/>).
///
/// Önceki hali (CompanyMembership) ikisini birden yapıyordu: hem üyelikti hem
/// üç basamaklı bir yetki merdiveniydi. Merdiven, şirketin kendi izin
/// kümelerini tanımlayabilmesinin önündeki engeldi — "müdür" bir unvan,
/// izin kümesi değil.
/// </summary>
public class CompanyMembership : BaseEntity
{
    public string KeycloakUserId { get; set; } = string.Empty;

    public Guid CompanyId { get; set; }

    /// <summary>
    /// <see cref="MembershipLevels"/>. Kurucu ve admin izin hesabının dışında;
    /// üye ise izinlerini rollerinden ve kişisel izinlerinden alır.
    /// </summary>
    public string Level { get; set; } = MembershipLevels.Member;

    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string? AssignedBy { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
}

/// <summary>
/// Üyelik seviyeleri. Bir yetki merdiveni değil: kurucu ve admin izin
/// şemasının tamamen dışında, üye ise tamamen içinde.
/// </summary>
public static class MembershipLevels
{
    /// <summary>
    /// Şirketi kuran e-posta. Şirket başına tek ve izinle daraltılamaz;
    /// kilitlenmeye karşı son güvence. Devri sonradan çift taraflı e-posta
    /// onayıyla yapılacak (bkz. Faz 7.5), bugün değiştirilemiyor.
    /// </summary>
    public const string Founder = "FOUNDER";

    /// <summary>
    /// Şirket yöneticisi. Kurucu gibi izin şemasının dışında ve her şeye
    /// erişir; kurucudan farkı çoğul olabilmesi ve geri alınabilmesi.
    /// </summary>
    public const string Admin = "ADMIN";

    /// <summary>
    /// Sıradan üye. Ne yapabildiği tamamen rollerinden ve kişisel
    /// izinlerinden geliyor; üyelik tek başına panele girmekten fazlasını
    /// vermiyor.
    /// </summary>
    public const string Member = "MEMBER";

    public static readonly string[] All = [Founder, Admin, Member];

    public static bool IsValid(string? level)
        => !string.IsNullOrWhiteSpace(level) && All.Contains(level);

    /// Kurucu ve admin izin hesabına hiç girmiyor.
    public static bool BypassesPermissions(string? level)
        => level is Founder or Admin;
}

/// <summary>
/// Firma kapsamının dışındaki roller. <see cref="MembershipLevels"/> bir
/// kullanıcının tek bir firma içindeki durumunu anlatır; buradaki rol ise
/// platformu işleten ekibi tanımlar ve tüm firmalar üzerinde geçerlidir.
/// </summary>
public static class PlatformRoles
{
    /// <summary>
    /// Grafirio personeli: müşteri firmalarının tamamını görür ve yönetir.
    /// ProjectAdmin bu rolle korunur.
    /// </summary>
    public const string PLATFORM_ADMIN = "PLATFORM_ADMIN";
}

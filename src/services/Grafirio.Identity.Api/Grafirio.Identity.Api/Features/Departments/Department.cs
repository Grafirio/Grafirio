using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Departments;

/// <summary>
/// Bir şirket içindeki organizasyon birimi.
///
/// Departman **şubeye** bağlı, kiracıya değil: alt şirketler ayrı tüzel
/// kişilik ve kendi veri tabanlarını yönetiyorlar, dolayısıyla "Ankara Şubesi /
/// Muhasebe" ile "İstanbul Şubesi / Muhasebe" ayrı kayıtlar. Şubeler arası
/// geçiş yetkisini departman değil <see cref="Users.UserCompanyRole"/>
/// belirliyor — iki kavram karışırsa "hangi şirkete girebilirim" sorusunun iki
/// ayrı cevabı olur.
///
/// Bu aşamada departman yalnızca gruplama: veri ve modül izinleri sonraki
/// fazlarda buraya bağlanacak.
/// </summary>
public class Department : BaseEntity
{
    public Guid CompanyId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// Şirket içinde benzersiz kısa kod; dış sistemlerle eşleşme buradan.
    public string? Code { get; set; }

    public string? Description { get; set; }

    /// Departman yöneticisi. Yetki taşımıyor — kimin sorumlu olduğunu
    /// söylüyor; yetkiler <see cref="Users.UserCompanyRole"/> tarafında.
    public string? ManagerKeycloakUserId { get; set; }

    /// Maliyet merkezi kodu; raporlarda kırılım için.
    public string? CostCenter { get; set; }

    /// <summary>
    /// Bu departmandaki kullanıcıların girebileceği modüller
    /// (bkz. <see cref="Permissions.AppModules"/>).
    ///
    /// Departman rolün tavanını <b>daraltır</b>, genişletemez: buraya "Üyelik"
    /// eklenmiş olması sıradan kullanıcıya fatura ekranını açmaz.
    ///
    /// Boş liste "kısıt yok" demek değil, "hiçbir modül" demek. Hiç departmanı
    /// olmayan kullanıcı ise rol tavanına düşüyor — departman atamak bilinçli
    /// bir daraltma, atamamak varsayılan.
    /// </summary>
    public List<string> Modules { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

using Grafirio.Identity.Api.Features.Permissions;
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
/// Departman gruplamanın yanında yetkinin de bir parçası: <see cref="Permissions"/>
/// üyelerinin rol tavanını daraltıyor (bkz. PermissionService).
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
    /// (bkz. <see cref="AppModules"/>).
    ///
    /// <b>Eski alan.</b> Yerini <see cref="Permissions"/> aldı; yalnızca
    /// <see cref="Permissions"/> boş olan kayıtlar için okunuyor. Silinmiyor
    /// çünkü depo MongoDB ve migration yok: alan kaldırılırsa mevcut
    /// departmanların izinleri okunamaz hale gelir. Yeni yazımlarda ikisi
    /// birlikte doldurulup tutarlı kalıyor.
    /// </summary>
    public List<string> Modules { get; set; } = [];

    /// <summary>
    /// Bu departmandaki kullanıcıların izinleri
    /// (bkz. <see cref="AppPermissions"/>, <c>MODÜL.AKSİYON</c>).
    ///
    /// Departman rolün tavanını <b>daraltır</b>, genişletemez: buraya
    /// "BILLING.MANAGE" eklenmiş olması sıradan kullanıcıya fatura ekranını
    /// açmaz.
    ///
    /// Boş liste "kısıt yok" demek değil, "hiçbir izin" demek. Hiç departmanı
    /// olmayan kullanıcı ise rol tavanına düşüyor — departman atamak bilinçli
    /// bir daraltma, atamamak varsayılan.
    /// </summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// Departmanın etkin izin kümesi. <see cref="Permissions"/> boşsa eski
    /// <see cref="Modules"/> listesi izne çevriliyor: modülü açık olan
    /// departman o modülün bütün aksiyonlarına sahipti, dolayısıyla çeviri
    /// kimsenin yetkisini daraltmıyor.
    ///
    /// Dönüşüm okuma anında yapılıyor, tek seferlik bir veri taşımayla değil:
    /// taşıma sırasında yazılmış ya da eski bir sürümden gelen kayıt yine
    /// doğru okunsun.
    /// </summary>
    public IEnumerable<string> EffectivePermissionKeys()
    {
        if (Permissions.Count > 0) return Permissions;

        return Modules
            .Where(AppModules.IsValid)
            .SelectMany(AppPermissions.ForModule)
            .Distinct();
    }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

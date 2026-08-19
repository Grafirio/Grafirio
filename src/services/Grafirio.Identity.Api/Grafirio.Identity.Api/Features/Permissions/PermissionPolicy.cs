using Grafirio.Identity.Api.Features.Users;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Permissions;

/// <summary>
/// İzin <b>politikası</b>: hangi rol nereye kadar çıkabilir ve departmandan
/// gelen liste nasıl temizlenir.
///
/// Sözlüğün kendisi burada değil, <see cref="AppPermissions"/> içinde ve o
/// paylaşılan pakette duruyor: ANALYSIS ve DATA_SOURCES modülleri
/// DataAnalysis.Api'de uygulanıyor, anahtarı iki serviste ayrı yazmak birinin
/// diğerinden habersiz değişmesi demekti.
///
/// Politika ise paylaşılmıyor. "COMPANY_MANAGER nereye kadar çıkar" sorusunun
/// tek bir sahibi olmalı; iki servis ayrı ayrı cevaplarsa aynı kullanıcı iki
/// ekranda iki farklı yetkiye sahip görünür.
/// </summary>
public static class PermissionPolicy
{
    /// <summary>
    /// Rolün sahip olabileceği azami izin kümesi.
    ///
    /// Departman bu tavanı <b>daraltır</b>, genişletemez — aksi halde departman
    /// tanımlamak yetki yükseltmenin yolu olurdu.
    /// </summary>
    public static IReadOnlySet<string> CeilingForRole(string? role) => role switch
    {
        PlatformRoles.PLATFORM_ADMIN => AllSet,
        CompanyRoles.COMPANY_ADMIN => AllSet,
        CompanyRoles.COMPANY_MANAGER => ManagerSet,
        CompanyRoles.COMPANY_USER => UserSet,
        _ => EmptySet
    };

    /// <summary>
    /// Departmana yazılacak izin listesini temizler.
    ///
    /// Tanınmayan anahtarlar süzülüyor: istemciden gelen serbest metnin izin
    /// kümesine sızması, ileride o metin gerçek bir anahtara dönüştüğünde sessiz
    /// bir yetki açılışı olurdu. <see cref="AppPermissions.PanelRead"/> de
    /// süzülüyor — panele giriş role bağlı, departmanın konusu değil.
    ///
    /// İzin listesi boş gelip modül listesi doluysa (eski istemci) modüller izne
    /// çevriliyor; böylece güncellenmemiş panel bir departmanın izinlerini
    /// sıfırlamıyor.
    /// </summary>
    public static List<string> Sanitize(IEnumerable<string>? permissions, IEnumerable<string>? modules)
    {
        var cleaned = (permissions ?? [])
            .Where(AppPermissions.IsValid)
            .Where(p => p != AppPermissions.PanelRead)
            .Distinct()
            .ToList();

        if (cleaned.Count > 0) return cleaned;

        return [.. (modules ?? [])
            .Where(AppModules.IsValid)
            .SelectMany(AppPermissions.ForModule)
            .Distinct()];
    }

    private static readonly HashSet<string> AllSet = [.. AppPermissions.All];

    /// <summary>
    /// Müdür veriyi ve analizi yönetir, kullanıcı açabilir; ama rol atamaz, alt
    /// şirket açmaz, şirket kimliğine ve faturaya dokunmaz. Departman kurmak
    /// yöneticinin işi — müdür yalnızca görür.
    /// </summary>
    private static readonly HashSet<string> ManagerSet =
    [
        AppPermissions.PanelRead,
        AppPermissions.AnalysisRead, AppPermissions.AnalysisCreate, AppPermissions.AnalysisDelete,
        AppPermissions.DataSourcesRead, AppPermissions.DataSourcesCreate,
        AppPermissions.DataSourcesUpdate, AppPermissions.DataSourcesDelete,
        AppPermissions.DocumentsRead, AppPermissions.DocumentsCreate, AppPermissions.DocumentsDelete,
        AppPermissions.CompanySettingsRead,
        AppPermissions.UsersRead, AppPermissions.UsersCreate,
        AppPermissions.DepartmentsRead
    ];

    /// Sıradan kullanıcı kendisine açılanı görür ve soru sorar.
    private static readonly HashSet<string> UserSet =
    [
        AppPermissions.PanelRead,
        AppPermissions.AnalysisRead, AppPermissions.AnalysisCreate,
        AppPermissions.DocumentsRead
    ];

    private static readonly HashSet<string> EmptySet = [];
}

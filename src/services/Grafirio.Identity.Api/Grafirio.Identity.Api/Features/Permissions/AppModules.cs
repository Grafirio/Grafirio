using Grafirio.Identity.Api.Features.Users;

namespace Grafirio.Identity.Api.Features.Permissions;

/// <summary>
/// Panelin modülleri — kullanıcının hangi alanlara girebileceği bu anahtarlarla
/// belirleniyor.
///
/// Liste tek yerde: daha önce yetki kontrolleri "bu kişi yönetici mi" diye
/// handler'lara dağılmıştı ve kuralı değiştirmek yirmi dosyaya dokunmak
/// demekti. Handler'lar artık rol değil <b>izin</b> soruyor.
/// </summary>
public static class AppModules
{
    /// Kanvas, dashboard, soru sorma. Uygulaması DataAnalysis.Api'de.
    public const string Analysis = "ANALYSIS";

    /// Veri kaynağı bağlama ve yönetme. Uygulaması DataAnalysis.Api'de.
    public const string DataSources = "DATA_SOURCES";

    public const string Documents = "DOCUMENTS";
    public const string CompanySettings = "COMPANY_SETTINGS";
    public const string UsersRoles = "USERS_ROLES";
    public const string Departments = "DEPARTMENTS";
    public const string Billing = "BILLING";

    public static readonly string[] All =
    [
        Analysis, DataSources, Documents, CompanySettings, UsersRoles, Departments, Billing
    ];

    public static bool IsValid(string? module)
        => !string.IsNullOrWhiteSpace(module) && All.Contains(module);

    /// <summary>
    /// Rolün erişebileceği azami modül kümesi.
    ///
    /// Departman bu tavanı <b>daraltır</b>, genişletemez: bir departmana
    /// "Üyelik" modülü eklenmiş olması sıradan kullanıcıya fatura ekranını
    /// açmaz. Aksi halde departman tanımlamak yetki yükseltmenin yolu olurdu.
    /// </summary>
    public static IReadOnlySet<string> CeilingForRole(string? role) => role switch
    {
        PlatformRoles.PLATFORM_ADMIN => AllSet,
        CompanyRoles.COMPANY_ADMIN => AllSet,
        CompanyRoles.COMPANY_MANAGER => ManagerSet,
        CompanyRoles.COMPANY_USER => UserSet,
        _ => EmptySet
    };

    private static readonly HashSet<string> AllSet = [.. All];

    /// Müdür veriyi ve analizi yönetir, ama şirket kimliğine, kullanıcı
    /// yetkilerine ve faturaya dokunmaz.
    private static readonly HashSet<string> ManagerSet =
        [Analysis, DataSources, Documents, Departments];

    /// Sıradan kullanıcı kendisine açılanı görür ve soru sorar.
    private static readonly HashSet<string> UserSet = [Analysis, Documents];

    private static readonly HashSet<string> EmptySet = [];
}

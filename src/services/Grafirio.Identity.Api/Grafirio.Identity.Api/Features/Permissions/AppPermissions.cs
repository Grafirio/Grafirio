using Grafirio.Identity.Api.Features.Users;

namespace Grafirio.Identity.Api.Features.Permissions;

/// <summary>
/// Panelin izin anahtarları: <c>MODÜL.AKSİYON</c>.
///
/// <see cref="AppModules"/> "hangi menüye girebilir" sorusunu cevaplıyordu;
/// buradaki anahtarlar "o menüde ne yapabilir" sorusunu ekliyor. Modül tek
/// başına açık/kapalı olduğunda "bağlantıyı görsün ama değiştirmesin" ya da
/// "kullanıcı listesini görsün ama rol atamasın" ifade edilemiyordu; yetki
/// kararı da handler'lara <c>HasRoleAsync(COMPANY_ADMIN)</c> olarak dağılmış
/// durumdaydı.
///
/// Anahtar biçimi metin: depo MongoDB, şemasız ve migration yok. Enum
/// kullanmak, alanı taşımayan eski belgelerin okunmasını kıran bir değer tipi
/// eklemek olurdu (bkz. <see cref="Subscriptions.Subscription.CreditBalance"/>).
/// </summary>
public static class AppPermissions
{
    // ── Panel kapısı ──────────────────────────────────────────────────────

    /// <summary>
    /// Panele giriş. <b>Departman bu izni kaldıramaz</b> — daraltma modül ve
    /// aksiyon seviyesinde kalıyor. Aksi halde yanlış bir departman ataması
    /// kullanıcıyı boş bir ekran yerine kapının dışında bırakır ve durumu
    /// düzeltecek kişi de aynı şekilde kilitlenebilir.
    /// </summary>
    public const string PanelRead = "PANEL.READ";

    // ── Analiz ────────────────────────────────────────────────────────────

    public const string AnalysisRead = "ANALYSIS.READ";

    /// Soru sormak, analiz koşturmak.
    public const string AnalysisCreate = "ANALYSIS.CREATE";
    public const string AnalysisDelete = "ANALYSIS.DELETE";

    // ── Veri kaynakları ───────────────────────────────────────────────────

    public const string DataSourcesRead = "DATA_SOURCES.READ";

    /// Sunucu bağlamak.
    public const string DataSourcesCreate = "DATA_SOURCES.CREATE";

    /// Bağlantıyı değiştirmek; tablo seçimi de buraya giriyor.
    public const string DataSourcesUpdate = "DATA_SOURCES.UPDATE";
    public const string DataSourcesDelete = "DATA_SOURCES.DELETE";

    // ── Belgeler ──────────────────────────────────────────────────────────

    public const string DocumentsRead = "DOCUMENTS.READ";
    public const string DocumentsCreate = "DOCUMENTS.CREATE";
    public const string DocumentsDelete = "DOCUMENTS.DELETE";

    // ── Şirket ayarları ───────────────────────────────────────────────────

    public const string CompanySettingsRead = "COMPANY_SETTINGS.READ";
    public const string CompanySettingsUpdate = "COMPANY_SETTINGS.UPDATE";

    /// <summary>
    /// Alt şirket açmak. Ayrı bir modül değil, ekranı Şirket Ayarları'nın
    /// içinde; ama ayrı bir izin, çünkü şirket bilgisini düzeltmekle yeni bir
    /// tüzel kişilik açmak aynı ağırlıkta işler değil.
    /// </summary>
    public const string CompanySettingsCreateChild = "COMPANY_SETTINGS.CREATE_CHILD";

    // ── Kullanıcılar ve yetkiler ──────────────────────────────────────────

    public const string UsersRead = "USERS_ROLES.READ";

    /// Kullanıcı kaydı.
    public const string UsersCreate = "USERS_ROLES.CREATE";

    /// <summary>
    /// Rol atamak ve kaldırmak. <see cref="UsersCreate"/>'ten ayrı: kullanıcı
    /// açmak müdürün işi olabilir, kimin yönetici olacağına karar vermek
    /// yöneticinin.
    /// </summary>
    public const string UsersManageRoles = "USERS_ROLES.MANAGE_ROLES";

    // ── Departmanlar ──────────────────────────────────────────────────────

    public const string DepartmentsRead = "DEPARTMENTS.READ";
    public const string DepartmentsCreate = "DEPARTMENTS.CREATE";
    public const string DepartmentsUpdate = "DEPARTMENTS.UPDATE";
    public const string DepartmentsDelete = "DEPARTMENTS.DELETE";

    /// Departmana kullanıcı almak, çıkarmak.
    public const string DepartmentsAssignMembers = "DEPARTMENTS.ASSIGN_MEMBERS";

    /// <summary>
    /// Departmanın izin kümesini değiştirmek. Üyelik atamaktan ayrı tutuluyor:
    /// üyelik yalnızca daraltır, izin kümesini düzenlemek ise yetkinin kendisini
    /// şekillendirmek.
    /// </summary>
    public const string DepartmentsManagePermissions = "DEPARTMENTS.MANAGE_PERMISSIONS";

    // ── Üyelik / fatura ───────────────────────────────────────────────────

    public const string BillingRead = "BILLING.READ";
    public const string BillingManage = "BILLING.MANAGE";

    public static readonly string[] All =
    [
        PanelRead,
        AnalysisRead, AnalysisCreate, AnalysisDelete,
        DataSourcesRead, DataSourcesCreate, DataSourcesUpdate, DataSourcesDelete,
        DocumentsRead, DocumentsCreate, DocumentsDelete,
        CompanySettingsRead, CompanySettingsUpdate, CompanySettingsCreateChild,
        UsersRead, UsersCreate, UsersManageRoles,
        DepartmentsRead, DepartmentsCreate, DepartmentsUpdate, DepartmentsDelete,
        DepartmentsAssignMembers, DepartmentsManagePermissions,
        BillingRead, BillingManage
    ];

    private static readonly HashSet<string> AllSet = [.. All];

    public static bool IsValid(string? permission)
        => !string.IsNullOrWhiteSpace(permission) && AllSet.Contains(permission);

    /// <summary>
    /// Anahtarın modül parçası: <c>"DATA_SOURCES.UPDATE"</c> → <c>"DATA_SOURCES"</c>.
    /// <see cref="PanelRead"/>'in <see cref="AppModules"/>'de karşılığı yok,
    /// onun için null döner.
    /// </summary>
    public static string? ModuleOf(string permission)
    {
        var dot = permission.IndexOf('.');
        if (dot <= 0) return null;

        var module = permission[..dot];
        return AppModules.IsValid(module) ? module : null;
    }

    /// <summary>
    /// Bir modülün bütün izinleri. Eski <see cref="Departments.Department.Modules"/>
    /// kayıtları buradan izin kümesine çevriliyor: modülü açık olan departman,
    /// o modülün bütün aksiyonlarına sahipti.
    /// </summary>
    public static IReadOnlyList<string> ForModule(string module) =>
        [.. All.Where(p => p.StartsWith(module + ".", StringComparison.Ordinal))];

    /// <summary>
    /// Departmana yazılacak izin listesini temizler.
    ///
    /// Tanınmayan anahtarlar süzülüyor: istemciden gelen serbest metnin izin
    /// kümesine sızması, ileride o metin gerçek bir anahtara dönüştüğünde
    /// sessiz bir yetki açılışı olurdu. <see cref="PanelRead"/> de süzülüyor —
    /// panele giriş role bağlı, departmanın konusu değil.
    ///
    /// İzin listesi boş gelip modül listesi doluysa (eski istemci) modüller
    /// izne çevriliyor; böylece güncellenmemiş panel bir departmanın izinlerini
    /// sıfırlamıyor.
    /// </summary>
    public static List<string> Sanitize(IEnumerable<string>? permissions, IEnumerable<string>? modules)
    {
        var cleaned = (permissions ?? [])
            .Where(IsValid)
            .Where(p => p != PanelRead)
            .Distinct()
            .ToList();

        if (cleaned.Count > 0) return cleaned;

        return [.. (modules ?? [])
            .Where(AppModules.IsValid)
            .SelectMany(ForModule)
            .Distinct()];
    }

    /// <summary>
    /// İzin listesinin dokunduğu modüller. Eski <c>Modules</c> alanını izinlerle
    /// tutarlı tutmak ve menüyü doldurmak için.
    /// </summary>
    public static List<string> ModulesOf(IEnumerable<string> permissions) =>
        [.. permissions
            .Select(ModuleOf)
            .Where(m => m is not null)
            .Select(m => m!)
            .Distinct()];

    /// <summary>
    /// Rolün sahip olabileceği azami izin kümesi.
    ///
    /// Departman bu tavanı <b>daraltır</b>, genişletemez — <see cref="AppModules"/>
    /// ile aynı kural. Aksi halde departman tanımlamak yetki yükseltmenin yolu
    /// olurdu.
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
    /// Müdür veriyi ve analizi yönetir, kullanıcı açabilir; ama rol atamaz,
    /// alt şirket açmaz, şirket kimliğine ve faturaya dokunmaz. Departman
    /// kurmak yöneticinin işi — müdür yalnızca görür. Bu kırılım yeni bir
    /// kısıt değil: departman uçları zaten yönetici istiyordu, modül listesi
    /// müdüre menüyü gösterip işi yapmasına izin vermiyordu.
    /// </summary>
    private static readonly HashSet<string> ManagerSet =
    [
        PanelRead,
        AnalysisRead, AnalysisCreate, AnalysisDelete,
        DataSourcesRead, DataSourcesCreate, DataSourcesUpdate, DataSourcesDelete,
        DocumentsRead, DocumentsCreate, DocumentsDelete,
        CompanySettingsRead,
        UsersRead, UsersCreate,
        DepartmentsRead
    ];

    /// Sıradan kullanıcı kendisine açılanı görür ve soru sorar.
    private static readonly HashSet<string> UserSet =
    [
        PanelRead,
        AnalysisRead, AnalysisCreate,
        DocumentsRead
    ];

    private static readonly HashSet<string> EmptySet = [];

    /// <summary>
    /// Departmana atanabilen izinler. <see cref="PanelRead"/> dışarıda:
    /// panele giriş role bağlı, departman daraltmasının konusu değil.
    /// </summary>
    public static readonly string[] Assignable = [.. All.Where(p => p != PanelRead)];
}

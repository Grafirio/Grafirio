using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Grafirio.Shared.Identity.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.Identity.Api.Features.Permissions;

public class PermissionService(
    AppDbContext context,
    ICompanyAccessService access,
    IIdentityService identityService,
    ILogger<PermissionService> logger)
    : IPermissionService
{
    /// Aynı istek içinde birden çok izin sorulabiliyor; şirket başına bir kez
    /// hesaplanıp saklanıyor.
    private readonly Dictionary<Guid, EffectivePermissions> _cache = [];

    public async Task<bool> CanAsync(Guid companyId, string permission, CancellationToken ct)
        => (await ForCompanyAsync(companyId, ct)).Permissions.Contains(permission);


    public async Task<EffectivePermissions> ForCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (_cache.TryGetValue(companyId, out var cached)) return cached;

        var level = await access.EffectiveLevelAsync(companyId, ct);

        // Şirkete erişimi yoksa izin de yok; çağıran tarafın ayrıca erişim
        // kontrolü yapmasına gerek kalmıyor.
        //
        // Bu dal SESSİZDİ ve teşhisi imkânsız kılıyordu. Kurucu bile olsa,
        // üyelik bulunamayan kullanıcı boş izin kümesi alıyor; aşağıdaki
        // kuruculuk muafiyetine hiç sıra gelmiyor. Dışarıdan görünen tek şey
        // "her uç reddediyor" oluyor ve sebep hiçbir yere yazılmıyordu.
        if (level is null)
        {
            logger.LogWarning(
                "Yetki reddi: kullanıcı {UserId} için {CompanyId} şirketinde etkin üyelik yok. " +
                "Boş izin kümesi dönülüyor; kuruculuk muafiyeti uygulanmıyor.",
                identityService.UserId, companyId);

            return Store(new EffectivePermissions(companyId, null, [], [], false));
        }

        // Kurucu, admin ve platform ekibi izin şemasının dışında: rol
        // atanmıyor, kişisel izin verilmiyor, daraltılamıyorlar. Kilitlenmeye
        // karşı son güvence bu — yanlış tanımlanmış bir izin kümesi şirketi
        // kendi panelinin dışında bırakmasın.
        if (MembershipLevels.BypassesPermissions(level) || level == PlatformRoles.PLATFORM_ADMIN)
        {
            return Store(Build(companyId, level, AppPermissions.All));
        }

        var userId = identityService.UserId.ToString();

        var roleIds = await context.UserRoles
            .Where(x => x.KeycloakUserId == userId && x.CompanyId == companyId && x.IsActive)
            .Select(x => x.RoleId)
            .ToListAsync(ct);

        var granted = new HashSet<string>();

        if (roleIds.Count > 0)
        {
            var roles = await context.Roles
                .Where(x => roleIds.Contains(x.Id) && x.IsActive)
                .ToListAsync(ct);

            // Birden fazla rol taşıyan kişide izinler birleşiyor. Kesişim
            // olsaydı ikinci bir rol vermek yetkiyi daraltırdı.
            foreach (var permission in roles.SelectMany(r => r.Permissions))
            {
                granted.Add(permission);
            }
        }

        // Kişisel izinler rollerin üstüne ekleniyor: biri hem Muhasebe
        // rolünde olup hem o rolde bulunmayan bir izni taşıyabilsin.
        var personal = await context.UserPermissions
            .FirstOrDefaultAsync(x => x.KeycloakUserId == userId && x.CompanyId == companyId, ct);

        if (personal is not null)
        {
            foreach (var permission in personal.Permissions) granted.Add(permission);
        }

        // Panele giriş üyelikle geliyor. Üye olup hiç izni olmayan biri boş
        // bir panel görür; kapının dışında kalmaz, çünkü bu durumu düzeltecek
        // kişiyle konuşabilmesi için önce içeride olması gerekiyor.
        granted.Add(AppPermissions.PanelRead);

        return Store(Build(companyId, level, granted));
    }

    /// Modül listesi ayrı tutulmuyor, izinlerden türetiliyor: iki liste ayrı
    /// hesaplanırsa menü ile uygulanan kural sessizce ayrışır.
    ///
    /// <c>RestrictedByDepartment</c> bugün her zaman <c>false</c>: bu serviste
    /// departmana göre daraltma YOK — sözleşme alanı, daraltmayı yapacak iş
    /// (Yetki Faz 8) yazılmadan önce eklenmiş. Uydurma bir değer değil,
    /// bugünün doğrusu; daraltma geldiğinde burası onunla birlikte değişir.
    private static EffectivePermissions Build(
        Guid companyId, string level, IReadOnlyCollection<string> permissions)
        => new(companyId, level, AppPermissions.ModulesOf(permissions), [.. permissions], false);

    /// <summary>
    /// Hesaplanan kümeyi saklar ve ne hesaplandığını yazar.
    ///
    /// Log şart: bu ucun cevabı başka bir servisin yetki kararı oluyor ve
    /// reddedildiğinde geriye "403" dışında hiçbir iz kalmıyordu. Seviye ile
    /// izin listesini birlikte görmek, üç ayrı arızayı tek bakışta ayırıyor:
    /// üyelik bulunamaması (seviye boş), muafiyetin çalışmaması (seviye
    /// FOUNDER/ADMIN ama liste kısa) ve rolün eksik olması (seviye MEMBER,
    /// liste yalnızca PANEL.READ).
    ///
    /// İzin adları gizli veri değil; kullanıcı kimliği dışında kişisel bilgi
    /// yazılmıyor.
    /// </summary>
    private EffectivePermissions Store(EffectivePermissions value)
    {
        logger.LogInformation(
            "Etkin yetki hesaplandı: kullanıcı {UserId}, şirket {CompanyId}, " +
            "seviye {Level}, muafiyet {Bypass}, {Count} izin: {Permissions}",
            identityService.UserId,
            value.CompanyId,
            value.Role ?? "(yok)",
            MembershipLevels.BypassesPermissions(value.Role),
            value.Permissions.Count,
            string.Join(", ", value.Permissions));

        return _cache[value.CompanyId] = value;
    }
}

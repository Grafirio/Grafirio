using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Grafirio.Shared.Identity.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.Identity.Api.Features.Permissions;

public class PermissionService(
    AppDbContext context,
    ICompanyAccessService access,
    IIdentityService identityService)
    : IPermissionService
{
    /// Aynı istek içinde birden çok izin sorulabiliyor; şirket başına bir kez
    /// hesaplanıp saklanıyor.
    private readonly Dictionary<Guid, EffectivePermissions> _cache = [];

    public async Task<bool> CanAsync(Guid companyId, string permission, CancellationToken ct)
        => (await ForCompanyAsync(companyId, ct)).Permissions.Contains(permission);

    public async Task<bool> CanSeeModuleAsync(Guid companyId, string module, CancellationToken ct)
        => (await ForCompanyAsync(companyId, ct)).Modules.Contains(module);

    public async Task<EffectivePermissions> ForCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (_cache.TryGetValue(companyId, out var cached)) return cached;

        var level = await access.EffectiveLevelAsync(companyId, ct);

        // Şirkete erişimi yoksa izin de yok; çağıran tarafın ayrıca erişim
        // kontrolü yapmasına gerek kalmıyor.
        if (level is null)
        {
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
    private static EffectivePermissions Build(
        Guid companyId, string level, IReadOnlyCollection<string> permissions)
        => new(companyId, level, AppPermissions.ModulesOf(permissions), [.. permissions],
            // Daraltma diye bir şey kalmadı: izinler birleşiyor, tavan yok.
            RestrictedByDepartment: false);

    private EffectivePermissions Store(EffectivePermissions value)
        => _cache[value.CompanyId] = value;
}

using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using Grafirio.Shared.Identity.Permissions;

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

        var role = await access.EffectiveRoleAsync(companyId, ct);

        // Şirkete erişimi yoksa izin de yok; çağıran tarafın ayrıca erişim
        // kontrolü yapmasına gerek kalmıyor.
        if (role is null)
        {
            return Store(new EffectivePermissions(companyId, null, [], [], false));
        }

        var ceiling = PermissionPolicy.CeilingForRole(role);

        // Yönetici departman kısıtından muaf: kendi şirketinde her şeyi
        // görmeli, aksi halde kendi kurduğu daraltmayla kendini kilitleyebilir.
        if (role is PlatformRoles.PLATFORM_ADMIN or CompanyRoles.COMPANY_ADMIN)
        {
            return Store(Build(companyId, role, ceiling, restricted: false));
        }

        var userId = identityService.UserId.ToString();

        var memberships = await context.UserDepartments
            .Where(x => x.KeycloakUserId == userId && x.CompanyId == companyId && x.IsActive)
            .ToListAsync(ct);

        // Hiç departmanı olmayan kullanıcı rol tavanına düşüyor. Departman
        // atamak bilinçli bir daraltma; atamamak "kısıtsız" demek. Tersi
        // olsaydı departman kavramı geldiği anda mevcut bütün kullanıcılar
        // panelden düşerdi.
        if (memberships.Count == 0)
        {
            return Store(Build(companyId, role, ceiling, restricted: false));
        }

        var departmentIds = memberships.Select(x => x.DepartmentId).Distinct().ToList();

        var departments = await context.Departments
            .Where(x => departmentIds.Contains(x.Id) && x.IsActive)
            .ToListAsync(ct);

        // Birden fazla departmandaysa izinler birleşiyor, sonra rol tavanıyla
        // kesişiyor — departman tavanı genişletemez.
        var granted = departments
            .SelectMany(d => d.EffectivePermissionKeys())
            .Where(ceiling.Contains)
            .ToHashSet();

        // Panele giriş role bağlı: departman modül ve aksiyonları daraltır ama
        // kapıyı kapatmaz. Aksi halde yanlış bir departman ataması kullanıcıyı
        // eksik bir panel yerine tamamen dışarıda bırakır ve durumu düzeltecek
        // kişi de aynı şekilde kilitlenebilir.
        if (ceiling.Contains(AppPermissions.PanelRead)) granted.Add(AppPermissions.PanelRead);

        return Store(Build(companyId, role, granted, restricted: true));
    }

    /// Modül listesi ayrı tutulmuyor, izinlerden türetiliyor: iki liste ayrı
    /// hesaplanırsa menü ile uygulanan kural sessizce ayrışır.
    private static EffectivePermissions Build(
        Guid companyId, string role, IReadOnlyCollection<string> permissions, bool restricted)
        => new(companyId, role, AppPermissions.ModulesOf(permissions), [.. permissions], restricted);

    private EffectivePermissions Store(EffectivePermissions value)
        => _cache[value.CompanyId] = value;
}

using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.Identity.Api.Features.Companies.Access;

public class CompanyAccessService(AppDbContext context, IIdentityService identityService)
    : ICompanyAccessService
{
    /// İstek boyunca aynı kullanıcının üyelikleri birden çok kez soruluyor
    /// (yetki kontrolü + liste + rol). Tek istek içinde bir kez okunuyor.
    private List<UserCompanyRole>? _memberships;

    private bool IsPlatformAdmin => identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);

    public async Task<bool> CanAccessAsync(Guid companyId, CancellationToken ct)
        => await EffectiveRoleAsync(companyId, ct) is not null;

    public async Task<bool> HasRoleAsync(Guid companyId, string role, CancellationToken ct)
    {
        if (IsPlatformAdmin) return true;

        var memberships = await GetMembershipsAsync(ct);
        if (memberships.Count == 0) return false;

        var path = await GetPathAsync(companyId, ct);
        if (path.Count == 0) return false;

        return memberships.Any(m => m.Role == role && path.Contains(m.CompanyId));
    }

    public async Task<string?> EffectiveRoleAsync(Guid companyId, CancellationToken ct)
    {
        if (IsPlatformAdmin) return PlatformRoles.PLATFORM_ADMIN;

        var memberships = await GetMembershipsAsync(ct);
        if (memberships.Count == 0) return null;

        var path = await GetPathAsync(companyId, ct);
        if (path.Count == 0) return null;

        // Birden fazla üyelik zincirde kesişirse en yetkilisi kazanır: üst
        // şirkette yönetici olup alt şirkette sıradan kullanıcı olarak da
        // eklenmiş biri yöneticiliğini kaybetmemeli.
        var matching = memberships.Where(m => path.Contains(m.CompanyId)).ToList();
        if (matching.Count == 0) return null;

        foreach (var role in RolesByPrecedence)
        {
            if (matching.Any(m => m.Role == role)) return role;
        }

        return matching[0].Role;
    }

    public async Task<List<Company>> AccessibleCompaniesAsync(CancellationToken ct)
    {
        if (IsPlatformAdmin)
        {
            return await context.Companies.Where(x => x.IsActive)
                .OrderBy(x => x.Level).ThenBy(x => x.Name).ToListAsync(ct);
        }

        var memberships = await GetMembershipsAsync(ct);
        if (memberships.Count == 0) return [];

        var membershipCompanyIds = memberships.Select(m => m.CompanyId).Distinct().ToList();

        // Path zincirinde üyelik şirketlerinden herhangi biri geçiyorsa erişim
        // var. Mongo tarafında bu dogal olarak { Path: { $in: [...] } } sorgusu.
        var companies = await context.Companies
            .Where(x => x.IsActive && x.Path.Any(p => membershipCompanyIds.Contains(p)))
            .ToListAsync(ct);

        return companies.OrderBy(x => x.Level).ThenBy(x => x.Name).ToList();
    }

    /// Yetkiden yetkisize; <see cref="EffectiveRoleAsync"/> ilk eşleşeni alıyor.
    private static readonly string[] RolesByPrecedence =
        [CompanyRoles.COMPANY_ADMIN, CompanyRoles.COMPANY_MANAGER, CompanyRoles.COMPANY_USER];

    private async Task<List<UserCompanyRole>> GetMembershipsAsync(CancellationToken ct)
    {
        if (_memberships is not null) return _memberships;

        if (identityService.UserId == Guid.Empty)
        {
            return _memberships = [];
        }

        var userId = identityService.UserId.ToString();

        return _memberships = await context.UserCompanyRoles
            .Where(x => x.KeycloakUserId == userId && x.IsActive)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Hedef şirketin kök zinciri. Path boşsa (henüz doldurulmamış eski kayıt)
    /// şirketin kendisiyle yetiniliyor — açılıştaki onarım bunu dolduruyor ama
    /// arada yazılmış bir kayıt yüzünden erişim tamamen kapanmasın.
    /// </summary>
    private async Task<List<Guid>> GetPathAsync(Guid companyId, CancellationToken ct)
    {
        var company = await context.Companies
            .FirstOrDefaultAsync(x => x.Id == companyId, ct);

        if (company is null) return [];

        return company.Path.Count > 0 ? company.Path : [company.Id];
    }
}

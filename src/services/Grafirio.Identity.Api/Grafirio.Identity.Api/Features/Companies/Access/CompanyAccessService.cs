using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.Identity.Api.Features.Companies.Access;

public class CompanyAccessService(
    AppDbContext context,
    IIdentityService identityService,
    ILogger<CompanyAccessService> logger)
    : ICompanyAccessService
{
    /// İstek boyunca aynı kullanıcının üyelikleri birden çok kez soruluyor
    /// (yetki kontrolü + liste + seviye). Tek istek içinde bir kez okunuyor.
    private List<CompanyMembership>? _memberships;

    private bool IsPlatformAdmin => identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);

    public async Task<bool> CanAccessAsync(Guid companyId, CancellationToken ct)
        => await EffectiveLevelAsync(companyId, ct) is not null;

    public async Task<string?> EffectiveLevelAsync(Guid companyId, CancellationToken ct)
    {
        if (IsPlatformAdmin) return PlatformRoles.PLATFORM_ADMIN;

        // Üç ayrı sebeple null dönülüyor ve üçü de SESSİZDİ. Dışarıdan hepsi
        // aynı görünüyor ("her uç reddediyor") ama yapılacak şey farklı:
        // üyeliğin hiç olmaması, şirketin bulunamaması ve üyeliğin başka bir
        // şirkete ait olması birbirine karıştırılamaz. Sebep yazılmadığı için
        // kurucu bir kullanıcının neden yetkisiz sayıldığı ancak veritabanına
        // elle bakarak anlaşılabiliyordu.
        var memberships = await GetMembershipsAsync(ct);
        if (memberships.Count == 0)
        {
            logger.LogWarning(
                "Erişim yok: kullanıcı {UserId} için hiç etkin üyelik kaydı yok " +
                "(sorulan şirket {CompanyId}).",
                identityService.UserId, companyId);
            return null;
        }

        var path = await GetPathAsync(companyId, ct);
        if (path.Count == 0)
        {
            logger.LogWarning(
                "Erişim yok: {CompanyId} şirketi veritabanında bulunamadı. " +
                "Kullanıcı {UserId} bu şirketi token'ındaki company_id claim'inden taşıyor olabilir.",
                companyId, identityService.UserId);
            return null;
        }

        // Birden fazla üyelik zincirde kesişirse en yetkilisi kazanır: üst
        // şirkette kurucu olup alt şirkette üye olarak da eklenmiş biri
        // kuruculuğunu kaybetmemeli.
        var matching = memberships.Where(m => path.Contains(m.CompanyId)).ToList();
        if (matching.Count == 0)
        {
            logger.LogWarning(
                "Erişim yok: kullanıcı {UserId} üye ama başka şirketlerde. " +
                "Üyelikleri: {MembershipCompanyIds}. Sorulan şirketin zinciri: {Path}.",
                identityService.UserId,
                string.Join(", ", memberships.Select(m => $"{m.CompanyId}:{m.Level}")),
                string.Join(", ", path));
            return null;
        }

        foreach (var level in LevelsByPrecedence)
        {
            if (matching.Any(m => m.Level == level)) return level;
        }

        return matching[0].Level;
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

    /// Yetkiden yetkisize; <see cref="EffectiveLevelAsync"/> ilk eşleşeni alıyor.
    private static readonly string[] LevelsByPrecedence =
        [MembershipLevels.Founder, MembershipLevels.Admin, MembershipLevels.Member];

    private async Task<List<CompanyMembership>> GetMembershipsAsync(CancellationToken ct)
    {
        if (_memberships is not null) return _memberships;

        if (identityService.UserId == Guid.Empty)
        {
            return _memberships = [];
        }

        var userId = identityService.UserId.ToString();

        return _memberships = await context.CompanyMemberships
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

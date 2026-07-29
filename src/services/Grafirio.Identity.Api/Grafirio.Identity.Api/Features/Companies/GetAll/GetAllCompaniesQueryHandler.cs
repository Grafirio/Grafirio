using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Dtos;
using Grafirio.Identity.Api.Features.Users;

namespace Grafirio.Identity.Api.Features.Companies.GetAll;

public class GetAllCompaniesQueryHandler(AppDbContext context, IIdentityService identityService, IMapper mapper)
    : IRequestHandler<GetAllCompaniesQuery, ServiceResult<List<CompanyDto>>>
{
    public async Task<ServiceResult<List<CompanyDto>>> Handle(GetAllCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        // Platform ekibi kiracı kapsamının disindadir: musterilerin tamamini gorur.
        // Aksi halde ProjectAdmin listeleyecek hicbir sey bulamaz, cunku
        // accessible_companies yalnizca kullanicinin kendi firmalarini tasir.
        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);

        var query = context.Companies.Where(x => x.IsActive);

        if (!isPlatformAdmin)
        {
            var accessibleCompanyIds = identityService.AccessibleCompanyIds;

            if (accessibleCompanyIds.Count == 0)
            {
                return ServiceResult<List<CompanyDto>>.SuccessAsOk(new List<CompanyDto>());
            }

            query = query.Where(x => accessibleCompanyIds.Contains(x.Id));
        }

        var companies = await query
            .OrderBy(x => x.Level)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var companiesDto = mapper.Map<List<CompanyDto>>(companies);

        return ServiceResult<List<CompanyDto>>.SuccessAsOk(companiesDto);
    }
}
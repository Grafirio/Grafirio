using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.GetAll;

public class GetAllCompaniesQueryHandler(AppDbContext context, IIdentityService identityService, IMapper mapper)
    : IRequestHandler<GetAllCompaniesQuery, ServiceResult<List<CompanyDto>>>
{
    public async Task<ServiceResult<List<CompanyDto>>> Handle(GetAllCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        // Get user's accessible company IDs
        var accessibleCompanyIds = identityService.AccessibleCompanyIds;
        
        if (accessibleCompanyIds.Count == 0)
        {
            return ServiceResult<List<CompanyDto>>.SuccessAsOk(new List<CompanyDto>());
        }

        var companies = await context.Companies
            .Where(x => x.IsActive && accessibleCompanyIds.Contains(x.Id))
            .OrderBy(x => x.Level)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var companiesDto = mapper.Map<List<CompanyDto>>(companies);

        return ServiceResult<List<CompanyDto>>.SuccessAsOk(companiesDto);
    }
}
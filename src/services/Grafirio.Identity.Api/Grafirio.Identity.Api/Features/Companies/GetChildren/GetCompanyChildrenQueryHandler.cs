using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Companies.Dtos;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.GetChildren;

public class GetCompanyChildrenQueryHandler(
    AppDbContext context,
    ICompanyAccessService access,
    IMapper mapper)
    : IRequestHandler<GetCompanyChildrenQuery, ServiceResult<List<CompanyDto>>>
{
    public async Task<ServiceResult<List<CompanyDto>>> Handle(GetCompanyChildrenQuery request,
        CancellationToken cancellationToken)
    {
        // Yetki üst şirketteki üyelikten geliyor ve hiyerarşik: kök şirketin
        // yöneticisi her şubenin altını görebiliyor. Alt şirketi görmek onun
        // içinde yetkili olmak anlamına gelmiyor.
        if (!await access.CanAccessAsync(request.CompanyId, cancellationToken))
        {
            return ServiceResult<List<CompanyDto>>.Error("Access denied to company",
                HttpStatusCode.Forbidden);
        }

        var children = await context.Companies
            .Where(x => x.ParentCompanyId == request.CompanyId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return ServiceResult<List<CompanyDto>>.SuccessAsOk(mapper.Map<List<CompanyDto>>(children));
    }
}

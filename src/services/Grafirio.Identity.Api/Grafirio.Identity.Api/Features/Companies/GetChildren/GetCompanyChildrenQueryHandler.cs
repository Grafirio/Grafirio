using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Dtos;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.GetChildren;

public class GetCompanyChildrenQueryHandler(
    AppDbContext context,
    IIdentityService identityService,
    IMapper mapper)
    : IRequestHandler<GetCompanyChildrenQuery, ServiceResult<List<CompanyDto>>>
{
    public async Task<ServiceResult<List<CompanyDto>>> Handle(GetCompanyChildrenQuery request,
        CancellationToken cancellationToken)
    {
        if (identityService.UserId == Guid.Empty)
        {
            return ServiceResult<List<CompanyDto>>.Error("Unauthenticated", HttpStatusCode.Unauthorized);
        }

        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);

        if (!isPlatformAdmin)
        {
            // Yetki üst şirketteki üyelikten geliyor: hiyerarşiyi görmek için
            // her alt şirkette ayrı üyelik aranmıyor. Alt şirketi görmek onun
            // içinde yetkili olmak anlamına gelmiyor; oradaki yetkiler kendi
            // üyelik kayıtlarıyla belirleniyor.
            var isMember = await context.UserCompanyRoles.AnyAsync(
                x => x.KeycloakUserId == identityService.UserId.ToString()
                     && x.CompanyId == request.CompanyId
                     && x.IsActive,
                cancellationToken);

            if (!isMember)
            {
                return ServiceResult<List<CompanyDto>>.Error("Access denied to company",
                    HttpStatusCode.Forbidden);
            }
        }

        var children = await context.Companies
            .Where(x => x.ParentCompanyId == request.CompanyId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return ServiceResult<List<CompanyDto>>.SuccessAsOk(mapper.Map<List<CompanyDto>>(children));
    }
}

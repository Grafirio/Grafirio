using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Companies.Dtos;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.GetCurrent;

public class GetCurrentCompanyQueryHandler(
    AppDbContext context,
    IIdentityService identityService,
    ICompanyAccessService access,
    IMapper mapper)
    : IRequestHandler<GetCurrentCompanyQuery, ServiceResult<CurrentCompanyResponse>>
{
    public async Task<ServiceResult<CurrentCompanyResponse>> Handle(GetCurrentCompanyQuery request,
        CancellationToken cancellationToken)
    {
        if (identityService.UserId == Guid.Empty)
        {
            return ServiceResult<CurrentCompanyResponse>.Error("Unauthenticated", HttpStatusCode.Unauthorized);
        }

        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);

        // Acik secim varsa once yetkisi dogrulaniyor: sirket degistirici
        // istemcide calisiyor, istenen kimlik dogrudan da gonderilebilir.
        if (request.CompanyId.HasValue)
        {
            var role = await access.EffectiveLevelAsync(request.CompanyId.Value, cancellationToken);

            if (role is null)
            {
                return ServiceResult<CurrentCompanyResponse>.Error("Access denied to company",
                    HttpStatusCode.Forbidden);
            }

            var selected = await context.Companies
                .FirstOrDefaultAsync(x => x.Id == request.CompanyId.Value, cancellationToken);

            if (selected is null)
            {
                return ServiceResult<CurrentCompanyResponse>.Error("Company not found",
                    HttpStatusCode.NotFound);
            }

            return ServiceResult<CurrentCompanyResponse>.SuccessAsOk(
                new CurrentCompanyResponse(mapper.Map<CompanyDto>(selected), role, isPlatformAdmin));
        }

        var accessible = await access.AccessibleCompaniesAsync(cancellationToken);

        if (accessible.Count == 0)
        {
            return ServiceResult<CurrentCompanyResponse>.Error("User is not assigned to a company",
                "Hesabınız henüz bir şirkete bağlı değil.", HttpStatusCode.NotFound);
        }

        // Secim yoksa koke en yakin sirket aciliyor; AccessibleCompaniesAsync
        // zaten Level'a gore siralanmis donuyor.
        var company = accessible[0];

        return ServiceResult<CurrentCompanyResponse>.SuccessAsOk(
            new CurrentCompanyResponse(
                mapper.Map<CompanyDto>(company),
                await access.EffectiveLevelAsync(company.Id, cancellationToken),
                isPlatformAdmin));
    }
}

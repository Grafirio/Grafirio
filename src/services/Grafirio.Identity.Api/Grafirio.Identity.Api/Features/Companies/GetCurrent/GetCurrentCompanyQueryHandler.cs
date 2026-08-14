using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Dtos;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.GetCurrent;

public class GetCurrentCompanyQueryHandler(
    AppDbContext context,
    IIdentityService identityService,
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

        var userId = identityService.UserId.ToString();

        var memberships = await context.UserCompanyRoles
            .Where(x => x.KeycloakUserId == userId && x.IsActive)
            .ToListAsync(cancellationToken);

        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);

        if (memberships.Count == 0)
        {
            // Platform ekibinin kendi üyeliği olmaz; token'ında bir firma
            // taşıyorsa onu gösteriyoruz, yoksa gerçekten bakacak bir şey yok.
            var claimCompanyId = identityService.CurrentCompanyId;

            if (!isPlatformAdmin || claimCompanyId is null)
            {
                return ServiceResult<CurrentCompanyResponse>.Error("User is not assigned to a company",
                    "Hesabınız henüz bir şirkete bağlı değil.", HttpStatusCode.NotFound);
            }

            var claimCompany = await context.Companies
                .FirstOrDefaultAsync(x => x.Id == claimCompanyId.Value, cancellationToken);

            if (claimCompany is null)
            {
                return ServiceResult<CurrentCompanyResponse>.Error("Company not found", HttpStatusCode.NotFound);
            }

            return ServiceResult<CurrentCompanyResponse>.SuccessAsOk(
                new CurrentCompanyResponse(mapper.Map<CompanyDto>(claimCompany),
                    PlatformRoles.PLATFORM_ADMIN, true));
        }

        // Birden fazla üyelik varsa token'ın işaret ettiği firma tercih edilir;
        // kullanıcı panelde firmalar arasında geçiş yaptığında seçimi orası
        // taşıyor. Claim yoksa ya da o firmanın üyeliği kapanmışsa en üstteki
        // (köke en yakın) firmaya düşülür.
        var preferred = identityService.CurrentCompanyId;
        var membership = memberships.FirstOrDefault(x => x.CompanyId == preferred) ?? memberships[0];

        var company = await context.Companies
            .FirstOrDefaultAsync(x => x.Id == membership.CompanyId, cancellationToken);

        if (company is null)
        {
            return ServiceResult<CurrentCompanyResponse>.Error("Company not found", HttpStatusCode.NotFound);
        }

        return ServiceResult<CurrentCompanyResponse>.SuccessAsOk(
            new CurrentCompanyResponse(mapper.Map<CompanyDto>(company), membership.Role, isPlatformAdmin));
    }
}

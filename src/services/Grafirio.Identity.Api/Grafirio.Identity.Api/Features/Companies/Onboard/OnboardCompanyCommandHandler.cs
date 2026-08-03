using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Onboard;

public class OnboardCompanyCommandHandler(
    AppDbContext context,
    IKeycloakUserService keycloakService,
    IIdentityService identityService)
    : IRequestHandler<OnboardCompanyCommand, ServiceResult<OnboardCompanyResponse>>
{
    public async Task<ServiceResult<OnboardCompanyResponse>> Handle(
        OnboardCompanyCommand request, CancellationToken cancellationToken)
    {
        if (identityService.UserId == Guid.Empty)
        {
            return ServiceResult<OnboardCompanyResponse>.Error("Unauthenticated",
                HttpStatusCode.Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<OnboardCompanyResponse>.Error("Name is required",
                "Çalışma alanı adı gerekli.", HttpStatusCode.BadRequest);
        }

        var userId = identityService.UserId.ToString();

        // Tek kullanimlik olmasinin sarti bu: uyeligi olan biri buradan yeni
        // firma acamaz, normal CreateCompany ucunu ve onun yetki kurallarini
        // kullanmak zorunda.
        var alreadyBelongs = await context.UserCompanyRoles
            .AnyAsync(x => x.KeycloakUserId == userId && x.IsActive, cancellationToken);

        if (alreadyBelongs)
        {
            return ServiceResult<OnboardCompanyResponse>.Error(
                "User already belongs to a company",
                "Zaten bir çalışma alanınız var.", HttpStatusCode.Conflict);
        }

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            var codeTaken = await context.Companies
                .AnyAsync(x => x.Code == request.Code, cancellationToken);

            if (codeTaken)
            {
                return ServiceResult<OnboardCompanyResponse>.Error("Company code already exists",
                    $"'{request.Code}' kodu kullanımda.", HttpStatusCode.BadRequest);
            }
        }

        var now = DateTime.UtcNow;

        var company = new Company
        {
            Id = NewId.NextSequentialGuid(),
            Name = request.Name.Trim(),
            Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim(),
            Description = request.Description,
            ParentCompanyId = null,
            Level = 0,
            IsActive = true,
            CreatedAt = now
        };

        var role = new UserCompanyRole
        {
            Id = NewId.NextSequentialGuid(),
            KeycloakUserId = userId,
            CompanyId = company.Id,
            Role = CompanyRoles.COMPANY_ADMIN,
            IsActive = true,
            AssignedAt = now,
            AssignedBy = identityService.UserName
        };

        await context.Companies.AddAsync(company, cancellationToken);
        await context.UserCompanyRoles.AddAsync(role, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // Yetki karari burada saklaniyor ama uygulanmasi token'daki claim'lere
        // bagli; Keycloak tarafindaki company_id / business_roles guncellenmezse
        // kullanici kendi kurdugu firmayi hic goremez.
        await keycloakService.AssignUserToCompanyAsync(userId, company.Id, CompanyRoles.COMPANY_ADMIN);

        return ServiceResult<OnboardCompanyResponse>.SuccessAsCreated(
            new OnboardCompanyResponse(company.Id, CompanyRoles.COMPANY_ADMIN),
            $"/api/v1/companies/{company.Id}");
    }
}

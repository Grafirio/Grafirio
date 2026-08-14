using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Update;

public class UpdateCompanyCommandHandler(AppDbContext context, IIdentityService identityService)
    : IRequestHandler<UpdateCompanyCommand, ServiceResult<UpdateCompanyResponse>>
{
    public async Task<ServiceResult<UpdateCompanyResponse>> Handle(UpdateCompanyCommand request,
        CancellationToken cancellationToken)
    {
        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);
        if (!isPlatformAdmin && !identityService.HasCompanyAccess(request.Id))
        {
            return ServiceResult<UpdateCompanyResponse>.Error("Access denied to company",
                HttpStatusCode.Forbidden);
        }

        var company = await context.Companies.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (company == null)
        {
            return ServiceResult<UpdateCompanyResponse>.Error("Company not found", HttpStatusCode.NotFound);
        }

        if (!string.IsNullOrEmpty(request.Code) && request.Code != company.Code)
        {
            var codeTaken = await context.Companies
                .AnyAsync(x => x.Code == request.Code && x.Id != request.Id, cancellationToken);

            if (codeTaken)
            {
                return ServiceResult<UpdateCompanyResponse>.Error("Company code already exists",
                    $"'{request.Code}' kodu kullanımda.", HttpStatusCode.BadRequest);
            }
        }

        company.Name = request.Name;
        company.Code = request.Code;
        company.Description = request.Description;
        company.TaxNumber = request.TaxNumber;
        company.TaxOffice = request.TaxOffice;
        company.TradeRegistryNumber = request.TradeRegistryNumber;
        company.MersisNumber = request.MersisNumber;
        company.CompanyType = request.CompanyType;
        company.EstablishmentDate = request.EstablishmentDate;
        company.ActivityCode = request.ActivityCode;
        company.ActivityDescription = request.ActivityDescription;
        company.LegalAddress = request.LegalAddress;
        company.KepAddress = request.KepAddress;
        company.AuthorizedSignatoryName = request.AuthorizedSignatoryName;
        company.AuthorizedSignatoryTitle = request.AuthorizedSignatoryTitle;
        company.KvkkRepresentativeName = request.KvkkRepresentativeName;
        company.KvkkRepresentativeEmail = request.KvkkRepresentativeEmail;
        company.GeneralPhone = request.GeneralPhone;
        company.GeneralEmail = request.GeneralEmail;
        company.BankAccounts = request.BankAccounts;
        company.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<UpdateCompanyResponse>.SuccessAsOk(new UpdateCompanyResponse(company.Id));
    }
}

using Grafirio.Identity.Api.Features.Companies.Access;

namespace Grafirio.Identity.Api.Features.Companies.Accessible;

public class GetAccessibleCompaniesQueryHandler(ICompanyAccessService access)
    : IRequestHandler<GetAccessibleCompaniesQuery, ServiceResult<List<AccessibleCompanyDto>>>
{
    public async Task<ServiceResult<List<AccessibleCompanyDto>>> Handle(
        GetAccessibleCompaniesQuery request, CancellationToken cancellationToken)
    {
        var companies = await access.AccessibleCompaniesAsync(cancellationToken);

        var result = new List<AccessibleCompanyDto>(companies.Count);

        foreach (var company in companies)
        {
            result.Add(new AccessibleCompanyDto(
                company.Id,
                company.Name,
                company.Code,
                company.CountryCode,
                company.ParentCompanyId,
                company.Level,
                await access.EffectiveLevelAsync(company.Id, cancellationToken)));
        }

        return ServiceResult<List<AccessibleCompanyDto>>.SuccessAsOk(result);
    }
}

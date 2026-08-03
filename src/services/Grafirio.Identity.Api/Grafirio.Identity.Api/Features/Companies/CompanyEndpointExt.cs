using Asp.Versioning.Builder;
using Grafirio.Identity.Api.Features.Companies.Create;
using Grafirio.Identity.Api.Features.Companies.GetAll;
using Grafirio.Identity.Api.Features.Companies.Onboard;

namespace Grafirio.Identity.Api.Features.Companies;

public static class CompanyEndpointExt
{
    public static void AddCompanyGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
    {
        app.MapGroup("api/v{version:apiVersion}/companies")
            .WithTags("Companies")
            .WithApiVersionSet(apiVersionSet)
            .CreateCompanyGroupItemEndpoint()
            .OnboardCompanyGroupItemEndpoint()
            .GetAllCompaniesGroupItemEndpoint()
            .MapToApiVersion(1, 0);
    }
}
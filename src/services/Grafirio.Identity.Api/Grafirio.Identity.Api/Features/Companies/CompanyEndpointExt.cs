using Asp.Versioning.Builder;
using Grafirio.Identity.Api.Features.Companies.Create;
using Grafirio.Identity.Api.Features.Companies.GetAll;

namespace Grafirio.Identity.Api.Features.Companies;

public static class CompanyEndpointExt
{
    public static void AddCompanyGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
    {
        app.MapGroup("api/v{version:apiVersion}/companies")
            .WithTags("Companies")
            .WithApiVersionSet(apiVersionSet)
            .CreateCompanyGroupItemEndpoint()
            .GetAllCompaniesGroupItemEndpoint()
            .MapToApiVersion(1, 0);
    }
}
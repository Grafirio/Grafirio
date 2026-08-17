using Asp.Versioning.Builder;
using Grafirio.Identity.Api.Features.Companies.Create;
using Grafirio.Identity.Api.Features.Companies.Documents.Delete;
using Grafirio.Identity.Api.Features.Companies.Documents.Download;
using Grafirio.Identity.Api.Features.Companies.Documents.GetAll;
using Grafirio.Identity.Api.Features.Companies.Documents.Upload;
using Grafirio.Identity.Api.Features.Companies.GetAll;
using Grafirio.Identity.Api.Features.Companies.GetChildren;
using Grafirio.Identity.Api.Features.Companies.GetCurrent;
using Grafirio.Identity.Api.Features.Companies.Onboard;
using Grafirio.Identity.Api.Features.Companies.Update;

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
            .GetCurrentCompanyGroupItemEndpoint()
            .GetCompanyChildrenGroupItemEndpoint()
            .UpdateCompanyGroupItemEndpoint()
            .UploadCompanyDocumentGroupItemEndpoint()
            .GetCompanyDocumentsGroupItemEndpoint()
            .DownloadCompanyDocumentGroupItemEndpoint()
            .DeleteCompanyDocumentGroupItemEndpoint()
            .MapToApiVersion(1, 0);
    }
}
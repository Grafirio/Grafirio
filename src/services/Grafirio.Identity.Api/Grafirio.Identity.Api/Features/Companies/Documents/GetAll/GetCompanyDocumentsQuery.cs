using Grafirio.Identity.Api.Features.Companies.Documents.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.Documents.GetAll;

public record GetCompanyDocumentsQuery(Guid CompanyId) : IRequestByServiceResult<List<CompanyDocumentDto>>;

namespace Grafirio.Identity.Api.Features.Companies.Documents.Delete;

public record DeleteCompanyDocumentCommand(Guid CompanyId, Guid DocumentId) : IRequestByServiceResult<bool>;

using Grafirio.Identity.Api.Features.Companies.Documents.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.Documents.Upload;

public record UploadCompanyDocumentCommand(
    Guid CompanyId,
    IFormFile File,
    string DocumentType,
    DateTime? ExpiryDate,
    string? Note
) : IRequestByServiceResult<CompanyDocumentDto>;

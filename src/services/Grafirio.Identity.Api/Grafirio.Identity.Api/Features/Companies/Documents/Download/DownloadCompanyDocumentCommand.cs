namespace Grafirio.Identity.Api.Features.Companies.Documents.Download;

public record DownloadCompanyDocumentCommand(Guid CompanyId, Guid DocumentId)
    : IRequestByServiceResult<DownloadCompanyDocumentResponse>;

public record DownloadCompanyDocumentResponse(byte[] Content, string ContentType, string FileName);

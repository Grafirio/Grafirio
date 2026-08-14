namespace Grafirio.Identity.Api.Features.Companies.Documents.Download;

/// <param name="Thumbnail">
/// Belgenin kendisi yerine küçük önizlemesi. Aynı erişim kontrolünden geçmesi
/// gerektiği için ayrı bir akış değil, aynı komutun bir bayrağı: önizleme de
/// belgenin içeriğini gösteriyor.
/// </param>
public record DownloadCompanyDocumentCommand(Guid CompanyId, Guid DocumentId, bool Thumbnail = false)
    : IRequestByServiceResult<DownloadCompanyDocumentResponse>;

public record DownloadCompanyDocumentResponse(byte[] Content, string ContentType, string FileName);

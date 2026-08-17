using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Documents.Download;

public class DownloadCompanyDocumentCommandHandler(
    AppDbContext context,
    ICompanyAccessService access,
    ICompanyDocumentStore store)
    : IRequestHandler<DownloadCompanyDocumentCommand, ServiceResult<DownloadCompanyDocumentResponse>>
{
    public async Task<ServiceResult<DownloadCompanyDocumentResponse>> Handle(
        DownloadCompanyDocumentCommand request, CancellationToken cancellationToken)
    {
        if (!await access.CanAccessAsync(request.CompanyId, cancellationToken))
        {
            return ServiceResult<DownloadCompanyDocumentResponse>.Error("Access denied to company",
                HttpStatusCode.Forbidden);
        }

        var document = await context.CompanyDocuments
            .FirstOrDefaultAsync(x => x.Id == request.DocumentId && x.CompanyId == request.CompanyId,
                cancellationToken);

        if (document is null)
        {
            return ServiceResult<DownloadCompanyDocumentResponse>.Error("Document not found",
                HttpStatusCode.NotFound);
        }

        // Önizleme istendiğinde ve belgede yoksa 404: istemci küçük resmi
        // isteyip isteyemeyeceğini listedeki HasThumbnail alanından biliyor,
        // bu yalnızca yarış durumuna karşı.
        var fileName = request.Thumbnail ? document.ThumbnailFileName : document.StoredFileName;
        if (string.IsNullOrEmpty(fileName))
        {
            return ServiceResult<DownloadCompanyDocumentResponse>.Error("Preview not available",
                HttpStatusCode.NotFound);
        }

        var content = await store.ReadAsync(request.CompanyId, fileName, cancellationToken);
        if (content is null)
        {
            // Yerel disk kalıcı değil: yeni sürüm yayınlandığında kayıt Mongo'da
            // kalır ama dosyanın kendisi gitmiş olur.
            return ServiceResult<DownloadCompanyDocumentResponse>.Error("File is no longer stored",
                "Dosya sunucuda bulunamadı.", HttpStatusCode.NotFound);
        }

        return request.Thumbnail
            ? ServiceResult<DownloadCompanyDocumentResponse>.SuccessAsOk(
                new DownloadCompanyDocumentResponse(content, "image/jpeg", document.OriginalFileName))
            : ServiceResult<DownloadCompanyDocumentResponse>.SuccessAsOk(
                new DownloadCompanyDocumentResponse(content,
                    document.ContentType ?? "application/octet-stream", document.OriginalFileName));
    }
}

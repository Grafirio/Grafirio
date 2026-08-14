using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Documents.Download;

public class DownloadCompanyDocumentCommandHandler(
    AppDbContext context,
    IIdentityService identityService,
    CompanyDocumentFileStorage storage)
    : IRequestHandler<DownloadCompanyDocumentCommand, ServiceResult<DownloadCompanyDocumentResponse>>
{
    public async Task<ServiceResult<DownloadCompanyDocumentResponse>> Handle(
        DownloadCompanyDocumentCommand request, CancellationToken cancellationToken)
    {
        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);
        if (!isPlatformAdmin && !identityService.HasCompanyAccess(request.CompanyId))
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

        var fileInfo = storage.GetFileInfo(request.CompanyId, document.StoredFileName);
        if (!fileInfo.Exists || fileInfo.PhysicalPath is null)
        {
            return ServiceResult<DownloadCompanyDocumentResponse>.Error("File not found on disk",
                HttpStatusCode.NotFound);
        }

        var content = await File.ReadAllBytesAsync(fileInfo.PhysicalPath, cancellationToken);

        return ServiceResult<DownloadCompanyDocumentResponse>.SuccessAsOk(
            new DownloadCompanyDocumentResponse(content, document.ContentType ?? "application/octet-stream",
                document.OriginalFileName));
    }
}

using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Documents.Delete;

public class DeleteCompanyDocumentCommandHandler(
    AppDbContext context,
    IIdentityService identityService,
    ICompanyDocumentStore store)
    : IRequestHandler<DeleteCompanyDocumentCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(DeleteCompanyDocumentCommand request,
        CancellationToken cancellationToken)
    {
        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);
        if (!isPlatformAdmin && !identityService.HasCompanyAccess(request.CompanyId))
        {
            return ServiceResult<bool>.Error("Access denied to company", HttpStatusCode.Forbidden);
        }

        var document = await context.CompanyDocuments
            .FirstOrDefaultAsync(x => x.Id == request.DocumentId && x.CompanyId == request.CompanyId,
                cancellationToken);

        if (document is null)
        {
            return ServiceResult<bool>.Error("Document not found", HttpStatusCode.NotFound);
        }

        context.CompanyDocuments.Remove(document);
        await context.SaveChangesAsync(cancellationToken);

        // Kayıt gittikten sonra dosyalar siliniyor; ters sırada yapılıp ikinci
        // adım düşerse geriye içeriği olmayan bir kayıt kalırdı.
        await store.DeleteAsync(request.CompanyId, document.StoredFileName, cancellationToken);

        if (!string.IsNullOrEmpty(document.ThumbnailFileName))
        {
            await store.DeleteAsync(request.CompanyId, document.ThumbnailFileName, cancellationToken);
        }

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

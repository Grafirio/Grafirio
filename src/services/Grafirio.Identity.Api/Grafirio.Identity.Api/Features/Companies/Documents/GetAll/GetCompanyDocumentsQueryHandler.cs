using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Documents.Dtos;
using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Documents.GetAll;

public class GetCompanyDocumentsQueryHandler(AppDbContext context, IPermissionService permissions, IMapper mapper)
    : IRequestHandler<GetCompanyDocumentsQuery, ServiceResult<List<CompanyDocumentDto>>>
{
    public async Task<ServiceResult<List<CompanyDocumentDto>>> Handle(GetCompanyDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.DocumentsRead, cancellationToken))
        {
            return ServiceResult<List<CompanyDocumentDto>>.Error("Access denied to module",
                "Belgeler modülüne erişiminiz yok.", HttpStatusCode.Forbidden);
        }

        var documents = await context.CompanyDocuments
            .Where(x => x.CompanyId == request.CompanyId)
            .OrderByDescending(x => x.UploadedAt)
            .ToListAsync(cancellationToken);

        return ServiceResult<List<CompanyDocumentDto>>.SuccessAsOk(mapper.Map<List<CompanyDocumentDto>>(documents));
    }
}

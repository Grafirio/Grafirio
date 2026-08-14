using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Documents.Dtos;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Documents.Upload;

public class UploadCompanyDocumentCommandHandler(
    AppDbContext context,
    IIdentityService identityService,
    CompanyDocumentFileStorage storage,
    IMapper mapper)
    : IRequestHandler<UploadCompanyDocumentCommand, ServiceResult<CompanyDocumentDto>>
{
    public async Task<ServiceResult<CompanyDocumentDto>> Handle(UploadCompanyDocumentCommand request,
        CancellationToken cancellationToken)
    {
        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);
        if (!isPlatformAdmin && !identityService.HasCompanyAccess(request.CompanyId))
        {
            return ServiceResult<CompanyDocumentDto>.Error("Access denied to company", HttpStatusCode.Forbidden);
        }

        var companyExists = await context.Companies.AnyAsync(x => x.Id == request.CompanyId, cancellationToken);
        if (!companyExists)
        {
            return ServiceResult<CompanyDocumentDto>.Error("Company not found", HttpStatusCode.NotFound);
        }

        if (request.File.Length == 0)
        {
            return ServiceResult<CompanyDocumentDto>.Error("File is empty", HttpStatusCode.BadRequest);
        }

        var storedFileName = await storage.SaveAsync(request.CompanyId, request.File, cancellationToken);

        var document = new CompanyDocument
        {
            Id = NewId.NextSequentialGuid(),
            CompanyId = request.CompanyId,
            DocumentType = request.DocumentType,
            OriginalFileName = request.File.FileName,
            StoredFileName = storedFileName,
            ContentType = request.File.ContentType,
            FileSizeBytes = request.File.Length,
            ExpiryDate = request.ExpiryDate,
            Note = request.Note,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = identityService.UserName
        };

        await context.CompanyDocuments.AddAsync(document, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<CompanyDocumentDto>.SuccessAsCreated(
            mapper.Map<CompanyDocumentDto>(document),
            $"/api/v1/companies/{request.CompanyId}/documents/{document.Id}");
    }
}

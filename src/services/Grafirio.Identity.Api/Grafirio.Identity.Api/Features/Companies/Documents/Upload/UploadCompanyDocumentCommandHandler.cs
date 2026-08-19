using AutoMapper;
using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Companies.Documents.Dtos;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Companies.Documents.Upload;

public class UploadCompanyDocumentCommandHandler(
    AppDbContext context,
    IIdentityService identityService,
    IPermissionService permissions,
    ICompanyDocumentStore store,
    CompanyDocumentThumbnailer thumbnailer,
    IMapper mapper)
    : IRequestHandler<UploadCompanyDocumentCommand, ServiceResult<CompanyDocumentDto>>
{
    /// Belge saklama alanı; bir şirketin tüm evrakı buna sığmalı ama tek bir
    /// dosya diski doldurabilecek boyutta olmamalı.
    private const long MaxFileSizeBytes = 25 * 1024 * 1024;

    public async Task<ServiceResult<CompanyDocumentDto>> Handle(UploadCompanyDocumentCommand request,
        CancellationToken cancellationToken)
    {
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.DocumentsCreate, cancellationToken))
        {
            return ServiceResult<CompanyDocumentDto>.Error("Insufficient permissions", "Belge yüklemek için bu şirkette belge ekleme izniniz olmalı.", HttpStatusCode.Forbidden);
        }

        var companyExists = await context.Companies.AnyAsync(x => x.Id == request.CompanyId, cancellationToken);
        if (!companyExists)
        {
            return ServiceResult<CompanyDocumentDto>.Error("Company not found", HttpStatusCode.NotFound);
        }

        if (request.File.Length == 0)
        {
            return ServiceResult<CompanyDocumentDto>.Error("File is empty",
                "Dosya boş.", HttpStatusCode.BadRequest);
        }

        if (request.File.Length > MaxFileSizeBytes)
        {
            return ServiceResult<CompanyDocumentDto>.Error("File is too large",
                $"Dosya {MaxFileSizeBytes / 1024 / 1024} MB sınırını aşıyor.", HttpStatusCode.BadRequest);
        }

        // Dosya belleğe bir kez alınıyor: hem saklamak hem önizleme üretmek
        // aynı içeriği okuyor, IFormFile akışını iki kez baştan sarmak yerine.
        using var buffer = new MemoryStream();
        await request.File.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();

        var extension = Path.GetExtension(request.File.FileName);
        var storedFileName = $"{Guid.NewGuid()}{extension}";

        await store.SaveAsync(request.CompanyId, storedFileName,
            new MemoryStream(content), cancellationToken);

        // Önizleme üretilemezse belge yine kaydediliyor; kart tipli bir rozetle
        // görünür, kullanıcı yüklemesini kaybetmez.
        string? thumbnailFileName = null;
        var thumbnail = thumbnailer.TryCreate(content, request.File.ContentType, request.File.FileName);
        if (thumbnail is not null)
        {
            thumbnailFileName = $"{Path.GetFileNameWithoutExtension(storedFileName)}-thumb.jpg";
            await store.SaveAsync(request.CompanyId, thumbnailFileName,
                new MemoryStream(thumbnail), cancellationToken);
        }

        var document = new CompanyDocument
        {
            Id = NewId.NextSequentialGuid(),
            CompanyId = request.CompanyId,
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType)
                ? CompanyDocumentTypes.Other
                : request.DocumentType,
            OriginalFileName = request.File.FileName,
            StoredFileName = storedFileName,
            ThumbnailFileName = thumbnailFileName,
            ContentType = request.File.ContentType,
            FileSizeBytes = request.File.Length,
            ExpiryDate = request.ExpiryDate,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
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

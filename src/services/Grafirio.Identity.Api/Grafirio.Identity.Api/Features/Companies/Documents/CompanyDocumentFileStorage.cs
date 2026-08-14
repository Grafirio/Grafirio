using Microsoft.Extensions.FileProviders;

namespace Grafirio.Identity.Api.Features.Companies.Documents;

/// <summary>
/// Şirket belgelerini diske yazar (Commerce.Api'nin FileService'iyle aynı
/// desen). Statik olarak dışarı açılmıyor — indirme, erişim kontrolü
/// yapabilen kimlik doğrulamalı bir uçtan (bkz. Download) akıtılıyor, çünkü
/// vergi levhası/imza sirküleri gibi belgeler herkese açık dosya sunucusunda
/// durmamalı.
/// </summary>
public class CompanyDocumentFileStorage(IFileProvider fileProvider)
{
    private const string RootFolder = "company-documents";

    public async Task<string> SaveAsync(Guid companyId, IFormFile file, CancellationToken ct)
    {
        var folder = Path.Combine(RootFolder, companyId.ToString());
        var physicalFolder = fileProvider.GetFileInfo(folder).PhysicalPath!;
        Directory.CreateDirectory(physicalFolder);

        var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var uploadPath = Path.Combine(physicalFolder, storedFileName);

        await using var stream = new FileStream(uploadPath, FileMode.Create);
        await file.CopyToAsync(stream, ct);

        return storedFileName;
    }

    public IFileInfo GetFileInfo(Guid companyId, string storedFileName)
        => fileProvider.GetFileInfo(Path.Combine(RootFolder, companyId.ToString(), storedFileName));

    public void Delete(Guid companyId, string storedFileName)
    {
        var fileInfo = GetFileInfo(companyId, storedFileName);
        if (fileInfo.Exists && fileInfo.PhysicalPath is not null)
        {
            File.Delete(fileInfo.PhysicalPath);
        }
    }
}

using Microsoft.Extensions.FileProviders;

namespace Grafirio.Identity.Api.Features.Companies.Documents;

/// <summary>
/// Belgeleri <c>wwwroot/company-documents/{companyId}/</c> altına yazar.
///
/// Klasör statik dosya olarak dışarı açılmıyor: vergi levhası ve imza
/// sirküleri gibi belgeler, adresi bilen herkesin indirebildiği bir yerde
/// durmamalı. İndirme, erişim kontrolü yapabilen kimlik doğrulamalı bir uçtan
/// akıtılıyor.
/// </summary>
public class LocalDiskCompanyDocumentStore(IFileProvider fileProvider) : ICompanyDocumentStore
{
    private const string RootFolder = "company-documents";

    public async Task<string> SaveAsync(Guid companyId, string fileName, Stream content, CancellationToken ct)
    {
        var physicalFolder = ResolveFolder(companyId);
        Directory.CreateDirectory(physicalFolder);

        var path = Path.Combine(physicalFolder, fileName);

        await using var target = new FileStream(path, FileMode.Create);
        await content.CopyToAsync(target, ct);

        return fileName;
    }

    public async Task<byte[]?> ReadAsync(Guid companyId, string fileName, CancellationToken ct)
    {
        var path = Path.Combine(ResolveFolder(companyId), fileName);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public Task DeleteAsync(Guid companyId, string fileName, CancellationToken ct)
    {
        var path = Path.Combine(ResolveFolder(companyId), fileName);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolveFolder(Guid companyId)
        => fileProvider.GetFileInfo(Path.Combine(RootFolder, companyId.ToString())).PhysicalPath!;
}

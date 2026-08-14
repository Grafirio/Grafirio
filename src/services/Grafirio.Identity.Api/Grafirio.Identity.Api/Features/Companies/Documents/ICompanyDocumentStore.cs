namespace Grafirio.Identity.Api.Features.Companies.Documents;

/// <summary>
/// Belge dosyalarının nerede durduğu.
///
/// Bugünkü uygulama container'ın yerel diski; orası kalıcı değil, yani her
/// yeni sürümde yüklenmiş belgeler kayboluyor. Bu bilinen ve kabul edilmiş bir
/// durum — kalıcı depoya (Azure Blob) geçiş bu arayüzün ikinci bir uygulaması
/// olsun diye çağıran taraf hiçbir yerde dosya yolu görmüyor.
/// </summary>
public interface ICompanyDocumentStore
{
    Task<string> SaveAsync(Guid companyId, string fileName, Stream content, CancellationToken ct);

    Task<byte[]?> ReadAsync(Guid companyId, string fileName, CancellationToken ct);

    Task DeleteAsync(Guid companyId, string fileName, CancellationToken ct);
}

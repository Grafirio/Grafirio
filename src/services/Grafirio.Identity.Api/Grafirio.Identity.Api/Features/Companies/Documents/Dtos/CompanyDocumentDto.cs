namespace Grafirio.Identity.Api.Features.Companies.Documents.Dtos;

public class CompanyDocumentDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Note { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }

    /// Önizleme ucunun bu belge için görsel döndüreceği. Dosya adının kendisi
    /// dışarı verilmiyor; istemcinin ihtiyacı olan tek şey resmi isteyip
    /// istemeyeceği.
    public bool HasThumbnail { get; set; }
}

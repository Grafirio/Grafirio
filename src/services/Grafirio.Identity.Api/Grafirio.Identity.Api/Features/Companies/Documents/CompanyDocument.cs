using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Companies.Documents;

public static class CompanyDocumentTypes
{
    public const string VergiLevhasi = "VERGI_LEVHASI";
    public const string ImzaSirkuleri = "IMZA_SIRKULERI";
    public const string TicaretSicilGazetesi = "TICARET_SICIL_GAZETESI";
    public const string FaaliyetBelgesi = "FAALIYET_BELGESI";
    public const string Diger = "DIGER";
}

public class CompanyDocument : BaseEntity
{
    public Guid CompanyId { get; set; }
    public string DocumentType { get; set; } = CompanyDocumentTypes.Diger;
    public string OriginalFileName { get; set; } = string.Empty;

    /// Diskteki gerçek dosya adı (GUID + uzantı) — indirmede CompanyId ile
    /// birlikte fiziksel yolu yeniden kurmak için kullanılır, dışarı verilmez.
    public string StoredFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Note { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedBy { get; set; }
}

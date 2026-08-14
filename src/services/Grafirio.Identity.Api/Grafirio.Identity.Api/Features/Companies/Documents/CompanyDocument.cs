using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Companies.Documents;

/// <summary>
/// Belge türleri ülkeden bağımsız adlandırıldı: "vergi levhası" yalnızca
/// Türkiye'de o adla var, ama karşılığı olan "vergi mükellefiyet belgesi" her
/// yerde var. İstemci bu kodları kullanıcının ülkesine göre yerelleştiriyor.
/// </summary>
public static class CompanyDocumentTypes
{
    /// Vergi mükellefiyet belgesi — TR: vergi levhası, US: EIN letter.
    public const string TaxCertificate = "TAX_CERTIFICATE";

    /// Kuruluş belgesi — TR: kuruluş ticaret sicil gazetesi,
    /// US: certificate of incorporation.
    public const string IncorporationCertificate = "INCORPORATION_CERTIFICATE";

    /// Sicil kaydı özeti — TR: faaliyet belgesi / sicil tasdiknamesi,
    /// GB: companies house extract.
    public const string RegistryExtract = "REGISTRY_EXTRACT";

    /// Ana sözleşme / esas mukavele.
    public const string ArticlesOfAssociation = "ARTICLES_OF_ASSOCIATION";

    /// İmza yetkisi belgesi — TR: imza sirküleri.
    public const string SignatureAuthorization = "SIGNATURE_AUTHORIZATION";

    /// KDV / VAT kaydı belgesi.
    public const string VatCertificate = "VAT_CERTIFICATE";

    /// Banka hesap teyit yazısı.
    public const string BankLetter = "BANK_LETTER";

    public const string Insurance = "INSURANCE";
    public const string License = "LICENSE";
    public const string Other = "OTHER";
}

public class CompanyDocument : BaseEntity
{
    public Guid CompanyId { get; set; }
    public string DocumentType { get; set; } = CompanyDocumentTypes.Other;
    public string OriginalFileName { get; set; } = string.Empty;

    /// Diskteki gerçek dosya adı (GUID + uzantı) — indirmede CompanyId ile
    /// birlikte fiziksel yolu yeniden kurmak için kullanılır, dışarı verilmez.
    public string StoredFileName { get; set; } = string.Empty;

    /// Önizleme görselinin dosya adı. Null olabilir: üretim başarısız olduğunda
    /// yükleme yine tamamlanıyor, yalnızca önizleme boş kalıyor.
    public string? ThumbnailFileName { get; set; }

    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Note { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedBy { get; set; }
}

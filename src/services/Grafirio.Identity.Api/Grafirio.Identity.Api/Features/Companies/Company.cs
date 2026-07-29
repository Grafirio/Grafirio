using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Companies;

public class Company : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
    public Guid? ParentCompanyId { get; set; }
    public int Level { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Müşteri, SQL bağlantısını "sa" gibi tam yetkili bir hesapla kaydettiğinde
    /// riskleri kabul ettiğine dair açık onayı. Onay firma seviyesinde tutulur;
    /// <see cref="Users.UserCompanyRole"/> ile aynı denetim izi desenini izler,
    /// böylece onayı kimin ne zaman verdiği sonradan sorulabilir.
    ///
    /// Nullable olmasının nedeni yalnızca teknik değil: null "hiç sorulmadı"
    /// demek ve bu, alan eklenmeden önce yazılmış kayıtların gerçek durumu.
    /// Zorunlu bool olarak eklendiğinde mevcut belgelerin okunması
    /// "Document element is missing" hatasıyla tamamen kırılmıştı.
    /// </summary>
    public bool? SaAccessConsentGiven { get; set; }
    public DateTime? SaAccessConsentGivenAt { get; set; }
    public string? SaAccessConsentGivenBy { get; set; }

    /// Onay alınırken kullanıcıya gösterilen metnin sürümü — metin değişirse
    /// eski onayların neyi kapsadığı belirsiz kalmasın.
    public string? SaAccessConsentTextVersion { get; set; }
}
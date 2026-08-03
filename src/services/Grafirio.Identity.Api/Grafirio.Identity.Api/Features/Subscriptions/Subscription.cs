using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Subscriptions;

/// <summary>
/// Bir firmanın satın aldığı erişim hakkı. UserAdmin'e girişi açan şey budur:
/// aktif aboneliği olmayan firmanın kullanıcısı ürünü kullanamaz.
///
/// Abonelik firma seviyesinde tutulur, kullanıcı seviyesinde değil — satın alma
/// ticari bir taahhüt ve firmanın altındaki kullanıcılar değiştikçe hakkın
/// kaybolmaması gerekiyor.
/// </summary>
public class Subscription : BaseEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>Satın alınan paket (bkz. <see cref="SubscriptionPlans"/>).</summary>
    public string Plan { get; set; } = string.Empty;

    /// <summary>Yaşam döngüsü durumu (bkz. <see cref="SubscriptionStatuses"/>).</summary>
    public string Status { get; set; } = SubscriptionStatuses.Active;

    public DateTime StartsAt { get; set; } = DateTime.UtcNow;

    /// <summary>Süresiz abonelikler için null.</summary>
    public DateTime? EndsAt { get; set; }

    /// <summary>
    /// Aboneliği doğuran sipariş. Ticaret tarafıyla bağı kurar; elle açılan
    /// aboneliklerde null kalır.
    /// </summary>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// Ücretsiz denemenin bittiği an. Yalnızca aylık pakette dolu; kredi
    /// paketinin denemesi yok, ön ödemeli çalışır.
    ///
    /// Erişim kontrolünü etkilemez: deneme boyunca abonelik zaten aktiftir.
    /// Ayrı tutulmasının nedeni faturalamanın ne zaman başlayacağının ve
    /// kullanıcıya kaç gün kaldığının buradan okunması.
    /// </summary>
    public DateTime? TrialEndsAt { get; set; }

    /// <summary>
    /// Kredi paketinde kalan bakiye. Yüklemeler burayı artırır.
    ///
    /// Bakiyeyi neyin azaltacağı henüz tanımlı değil; tüketim kuralı
    /// belirlendiğinde düşme mantığı eklenecek. O güne kadar alan yalnızca
    /// yüklenen tutarı taşır ve panelde gösterilir.
    ///
    /// Nullable olması şart. Depo MongoDB ve alan zorunlu bir değer tipi
    /// olarak eklendiğinde, bu alanı taşımayan mevcut belgelerin okunması
    /// "Document element is missing" ile tamamen kırılıyor —
    /// <see cref="Companies.Company.SaAccessConsentGiven"/> aynı sebeple
    /// nullable. null burada "hiç yükleme yapılmadı" demek.
    /// </summary>
    public decimal? CreditBalance { get; set; }

    // UserCompanyRole ile ayni denetim izi: kim ne zaman actı/kapattı.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }

    /// <summary>
    /// Şu an geçerli mi? Süresi dolmuş bir kayıt hâlâ Active durumda olabilir
    /// (kimse kapatmamıştır), bu yüzden tarih kontrolü de yapılır.
    /// </summary>
    public bool IsCurrentlyActive(DateTime? asOf = null)
    {
        var now = asOf ?? DateTime.UtcNow;
        return Status == SubscriptionStatuses.Active
               && StartsAt <= now
               && (EndsAt is null || EndsAt > now);
    }

    /// <summary>Ücretsiz deneme sürüyor mu?</summary>
    public bool IsInTrial(DateTime? asOf = null)
        => TrialEndsAt is not null && TrialEndsAt > (asOf ?? DateTime.UtcNow);
}

public static class SubscriptionPlans
{
    /// <summary>Aylık $15. İlk 15 gün ücretsiz; deneme bitince ücretlendirme başlar.</summary>
    public const string Monthly = "MONTHLY";

    /// <summary>Ön ödemeli bakiye. Denemesi yok, yüklediği kadar kullanır.</summary>
    public const string Credit = "CREDIT";

    // Eski kayitlar. Musteriye artik satilmiyor ama veritabaninda duruyorlar ve
    // dogrulamadan gecemezlerse mevcut firmalarin erisimi bir anda kapanir.
    public const string Trial = "TRIAL";
    public const string Standard = "STANDARD";
    public const string Enterprise = "ENTERPRISE";

    /// <summary>Musterinin kendi secebilecegi paketler.</summary>
    public static readonly string[] Sellable = [Monthly, Credit];

    public static readonly string[] All = [Monthly, Credit, Trial, Standard, Enterprise];

    public static bool IsValid(string plan) => All.Contains(plan);

    public static bool IsSellable(string plan) => Sellable.Contains(plan);
}

/// <summary>Aylık paketin ücretsiz deneme süresi.</summary>
public static class SubscriptionTrial
{
    public const int Days = 15;
}

public static class SubscriptionStatuses
{
    public const string Active = "ACTIVE";
    public const string Cancelled = "CANCELLED";
    public const string Expired = "EXPIRED";

    public static readonly string[] All = [Active, Cancelled, Expired];

    public static bool IsValid(string status) => All.Contains(status);
}

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
}

public static class SubscriptionPlans
{
    public const string Trial = "TRIAL";
    public const string Standard = "STANDARD";
    public const string Enterprise = "ENTERPRISE";

    public static readonly string[] All = [Trial, Standard, Enterprise];

    public static bool IsValid(string plan) => All.Contains(plan);
}

public static class SubscriptionStatuses
{
    public const string Active = "ACTIVE";
    public const string Cancelled = "CANCELLED";
    public const string Expired = "EXPIRED";

    public static readonly string[] All = [Active, Cancelled, Expired];

    public static bool IsValid(string status) => All.Contains(status);
}

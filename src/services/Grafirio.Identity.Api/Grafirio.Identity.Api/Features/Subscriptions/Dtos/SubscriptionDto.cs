namespace Grafirio.Identity.Api.Features.Subscriptions.Dtos;

public class SubscriptionDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Plan { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public Guid? OrderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }

    /// <summary>Yalnızca aylık pakette dolu; Üyelik Bilgileri sayfasının deneme geri sayımı için.</summary>
    public DateTime? TrialEndsAt { get; set; }

    /// <summary>Yalnızca kredi paketinde dolu.</summary>
    public decimal? CreditBalance { get; set; }

    /// <summary>Durum ve tarih birlikte değerlendirildiğinde şu an geçerli mi.</summary>
    public bool IsCurrentlyActive { get; set; }
}

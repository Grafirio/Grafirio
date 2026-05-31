namespace Grafirio.Commerce.Api.Modules.Payments;

public class PaymentEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string OrderCode { get; set; } = default!;
    public DateTime Created { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
}

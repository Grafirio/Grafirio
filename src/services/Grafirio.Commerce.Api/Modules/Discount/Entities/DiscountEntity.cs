namespace Grafirio.Commerce.Api.Modules.Discount;

public class DiscountEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public float Rate { get; set; }
    public string Code { get; set; } = default!;
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime Expired { get; set; }
}

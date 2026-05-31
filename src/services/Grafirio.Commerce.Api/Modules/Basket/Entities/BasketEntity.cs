namespace Grafirio.Commerce.Api.Modules.Basket;

public class BasketEntity
{
    public Guid UserId { get; set; }
    public List<BasketItemEntity> Items { get; set; } = [];
    public float? DiscountRate { get; set; }
    public string? Coupon { get; set; }

    public bool IsDiscountApplied => DiscountRate is > 0 && !string.IsNullOrEmpty(Coupon);
    public decimal TotalPrice => Items.Sum(x => x.Price);
    public decimal? TotalPriceWithDiscount => !IsDiscountApplied
        ? null
        : Items.Sum(x => x.DiscountedPrice ?? x.Price);

    public BasketEntity() { }
    public BasketEntity(Guid userId, List<BasketItemEntity> items) { UserId = userId; Items = items; }

    public void ApplyDiscount(string coupon, float rate)
    {
        Coupon = coupon;
        DiscountRate = rate;
        foreach (var item in Items)
            item.DiscountedPrice = item.Price * (decimal)(1 - rate);
    }

    public void ApplyExistingDiscount()
    {
        if (!IsDiscountApplied) return;
        foreach (var item in Items)
            item.DiscountedPrice = item.Price * (decimal)(1 - DiscountRate!.Value);
    }

    public void ClearDiscount() { Coupon = null; DiscountRate = null; }
}

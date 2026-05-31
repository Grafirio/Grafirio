namespace Grafirio.Commerce.Api.Modules.Basket;

internal static class BasketMapper
{
    public static BasketDto ToDto(BasketEntity b) => new(
        b.UserId,
        b.Items.Select(i => new BasketItemDto(i.Id, i.Name, i.ImageUrl, i.Price, i.DiscountedPrice)).ToList(),
        b.DiscountRate,
        b.Coupon,
        b.TotalPrice,
        b.TotalPriceWithDiscount);
}

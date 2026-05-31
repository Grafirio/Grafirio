namespace Grafirio.Commerce.Api.Modules.Basket;

public record BasketDto(
    Guid UserId,
    List<BasketItemDto> Items,
    float? DiscountRate,
    string? Coupon,
    decimal TotalPrice,
    decimal? TotalPriceWithDiscount);

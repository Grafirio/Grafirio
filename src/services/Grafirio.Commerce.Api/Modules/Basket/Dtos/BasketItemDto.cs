namespace Grafirio.Commerce.Api.Modules.Basket;

public record BasketItemDto(
    Guid Id,
    string Name,
    string? ImageUrl,
    decimal Price,
    decimal? DiscountedPrice);

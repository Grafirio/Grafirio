namespace Grafirio.Commerce.Api.Modules.Basket;

public record AddBasketItemRequest(
    Guid ProductId,
    string ProductName,
    decimal Price,
    string? ImageUrl);

public class AddBasketItemValidator : AbstractValidator<AddBasketItemRequest>
{
    public AddBasketItemValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ProductName).NotEmpty();
        RuleFor(x => x.Price).GreaterThan(0);
    }
}

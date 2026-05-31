namespace Grafirio.Commerce.Api.Modules.Orders;

public record CreateOrderRequest(
    float? DiscountRate,
    AddressRequest Address,
    List<OrderItemRequest> Items);

public record AddressRequest(
    string Province,
    string District,
    string Street,
    string ZipCode,
    string Line);

public record OrderItemRequest(Guid ProductId, string ProductName, decimal UnitPrice);

public class CreateOrderValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must have at least one item");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.ProductId).NotEmpty();
            item.RuleFor(x => x.ProductName).NotEmpty();
            item.RuleFor(x => x.UnitPrice).GreaterThan(0);
        });
        RuleFor(x => x.Address).NotNull();
        RuleFor(x => x.Address.Province).NotEmpty();
        RuleFor(x => x.Address.District).NotEmpty();
    }
}

namespace Grafirio.Commerce.Api.Modules.Basket;

public record ApplyDiscountRequest(string Coupon, float Rate);

public class ApplyDiscountValidator : AbstractValidator<ApplyDiscountRequest>
{
    public ApplyDiscountValidator()
    {
        RuleFor(x => x.Coupon).NotEmpty().Length(10);
        RuleFor(x => x.Rate).GreaterThan(0).LessThan(1);
    }
}

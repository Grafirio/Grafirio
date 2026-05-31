namespace Grafirio.Commerce.Api.Modules.Discount;

public record CreateDiscountRequest(string Code, float Rate, Guid UserId, DateTime Expired);

public class CreateDiscountValidator : AbstractValidator<CreateDiscountRequest>
{
    public CreateDiscountValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(10);
        RuleFor(x => x.Rate).GreaterThan(0).LessThan(1);
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Expired).GreaterThan(DateTime.UtcNow);
    }
}

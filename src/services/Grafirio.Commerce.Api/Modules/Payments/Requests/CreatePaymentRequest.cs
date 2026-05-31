namespace Grafirio.Commerce.Api.Modules.Payments;

public record CreatePaymentRequest(
    string OrderCode,
    string CardNumber,
    string CardHolderName,
    string CardExpirationDate,
    string CardSecurityNumber,
    decimal Amount);

public class CreatePaymentValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.OrderCode).NotEmpty().Length(10);
        RuleFor(x => x.CardNumber).NotEmpty().CreditCard();
        RuleFor(x => x.CardHolderName).NotEmpty();
        RuleFor(x => x.CardExpirationDate).NotEmpty();
        RuleFor(x => x.CardSecurityNumber).NotEmpty().Length(3, 4);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

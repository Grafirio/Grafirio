using MediatR;
using Grafirio.Shared;

namespace Grafirio.Payment.Api.Feature.Payments.Create
{
    public record CreatePaymentCommand(
        string OrderCode,
        string CardNumber,
        string CardHolderName,
        string CardExpirationDate,
        string CardSecurityNumber,
        decimal Amount) : IRequestByServiceResult<Guid>;
}
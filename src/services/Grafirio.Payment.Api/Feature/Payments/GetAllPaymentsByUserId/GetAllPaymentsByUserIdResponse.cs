using Grafirio.Payment.Api.Repositories;

namespace Grafirio.Payment.Api.Feature.Payments.GetAllPaymentsByUserId
{
    public record GetAllPaymentsByUserIdResponse(
        Guid Id,
        string OrderCode,
        string Amount,
        DateTime Created,
        PaymentStatus Status);
}
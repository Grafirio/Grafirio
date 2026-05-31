namespace Grafirio.Commerce.Api.Modules.Payments;

public record PaymentSummaryDto(
    Guid Id,
    string OrderCode,
    string Amount,
    DateTime Created,
    PaymentStatus Status);

namespace Grafirio.Commerce.Api.Modules.Payments;

public class PaymentService(PaymentDbContext db, IIdentityService identity)
{
    public async Task<ServiceResult<Guid>> CreateAsync(CreatePaymentRequest req, CancellationToken ct)
    {
        var (ok, error) = await ProcessExternalPaymentAsync(req);
        if (!ok)
            return ServiceResult<Guid>.Error("Payment failed", error!, HttpStatusCode.BadRequest);

        var payment = new PaymentEntity
        {
            Id        = NewId.NextSequentialGuid(),
            UserId    = identity.UserId,
            OrderCode = req.OrderCode,
            Amount    = req.Amount,
            Created   = DateTime.UtcNow,
            Status    = PaymentStatus.Success
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        return ServiceResult<Guid>.SuccessAsOk(payment.Id);
    }

    public async Task<ServiceResult<List<PaymentSummaryDto>>> GetMyPaymentsAsync(CancellationToken ct)
    {
        var payments = await db.Payments
            .Where(x => x.UserId == identity.UserId)
            .OrderByDescending(x => x.Created)
            .Select(x => new PaymentSummaryDto(
                x.Id, x.OrderCode, x.Amount.ToString("C"), x.Created, x.Status))
            .ToListAsync(ct);

        return ServiceResult<List<PaymentSummaryDto>>.SuccessAsOk(payments);
    }

    // Gerçek ödeme entegrasyonu buraya gelecek
    private static Task<(bool ok, string? error)> ProcessExternalPaymentAsync(CreatePaymentRequest req)
        => Task.FromResult<(bool, string?)>((true, null));
}

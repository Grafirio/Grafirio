using Grafirio.Commerce.Api.Modules.Catalog;
using Grafirio.Commerce.Api.Modules.Orders;
using Grafirio.Contracts.Commerce;

namespace Grafirio.Commerce.Api.Modules.Payments;

public class PaymentService(
    PaymentDbContext db,
    OrderDbContext orders,
    CatalogDbContext catalog,
    IPublishEndpoint publishEndpoint,
    IIdentityService identity,
    ILogger<PaymentService> logger)
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

        await CompleteOrderAsync(payment, ct);

        return ServiceResult<Guid>.SuccessAsOk(payment.Id);
    }

    /// <summary>
    /// Siparişi ödendi olarak işaretler ve sonucu duyurur.
    ///
    /// Ödeme kaydı alınmış olduğu için buradaki bir aksaklık çağrıyı başarısız
    /// saymaz: parasını ödemiş müşteriye hata göstermek yanlış olur. Bedeli,
    /// sipariş durumunun geride kalabilmesi; bu yüzden sessizce yutulmuyor,
    /// hata olarak loglanıyor.
    /// </summary>
    private async Task CompleteOrderAsync(PaymentEntity payment, CancellationToken ct)
    {
        try
        {
            var order = await orders.Orders
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Code == payment.OrderCode, ct);

            if (order is null)
            {
                logger.LogError(
                    "Ödeme alındı ama sipariş bulunamadı. OrderCode={OrderCode}, PaymentId={PaymentId}",
                    payment.OrderCode, payment.Id);
                return;
            }

            order.SetPaid(payment.Id);
            await orders.SaveChangesAsync(ct);

            var plans = await ResolveSubscriptionPlansAsync(order, ct);

            await publishEndpoint.Publish<IOrderPaid>(new OrderPaid(
                order.Id,
                order.Code,
                order.BuyerId.ToString(),
                payment.Id,
                payment.Amount,
                payment.Created,
                plans), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Sipariş tamamlanamadı. OrderCode={OrderCode}, PaymentId={PaymentId}",
                payment.OrderCode, payment.Id);
        }
    }

    /// <summary>
    /// Siparişteki kalemlerden abonelik planına karşılık gelenleri toplar.
    /// Planlar katalogda ürün olarak durduğu için bilgi ürünün kendisinden
    /// okunur; sıradan ürünlerde boş liste döner ve sipariş erişim doğurmaz.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveSubscriptionPlansAsync(Order order, CancellationToken ct)
    {
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        if (productIds.Count == 0) return [];

        return await catalog.Products
            .Where(p => productIds.Contains(p.Id) && p.SubscriptionPlan != null)
            .Select(p => p.SubscriptionPlan!)
            .ToListAsync(ct);
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

    // Gerçek ödeme entegrasyonu buraya gelecek. Sağlayıcı henüz seçilmediği
    // için akışın geri kalanı ondan bağımsız kuruldu: buraya bir sağlayıcı
    // takıldığında başka hiçbir yerin değişmesi gerekmiyor.
    private static Task<(bool ok, string? error)> ProcessExternalPaymentAsync(CreatePaymentRequest req)
        => Task.FromResult<(bool, string?)>((true, null));
}

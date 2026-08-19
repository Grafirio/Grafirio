using Grafirio.Contracts.Commerce;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.Identity.Api.Features.Subscriptions;

/// <summary>
/// Ödemesi alınan bir siparişi erişim hakkına çevirir.
///
/// Ticaret tarafı abonelik kavramını bilmez; yalnızca neyin satın alındığını
/// duyurur. Satın almanın kime yazılacağı burada belirlenir: alıcı kullanıcının
/// bağlı olduğu firma bulunur ve abonelik o firmaya açılır.
/// </summary>
public class OrderPaidConsumer(AppDbContext context, ILogger<OrderPaidConsumer> logger)
    : IConsumer<IOrderPaid>
{
    public async Task Consume(ConsumeContext<IOrderPaid> context_)
    {
        var message = context_.Message;

        if (message.SubscriptionPlans.Count == 0)
        {
            // Siradan urun siparisi; erisimle ilgisi yok.
            return;
        }

        // Ayni mesaj yeniden teslim edilebilir (MassTransit en-az-bir-kez
        // garanti eder). Siparis basina tek abonelik acilsin.
        var alreadyHandled = await context.Subscriptions
            .AnyAsync(x => x.OrderId == message.OrderId, context_.CancellationToken);

        if (alreadyHandled)
        {
            logger.LogInformation("Sipariş zaten işlenmiş, atlanıyor. OrderId={OrderId}", message.OrderId);
            return;
        }

        var membership = await context.CompanyMemberships
            .FirstOrDefaultAsync(x => x.KeycloakUserId == message.BuyerId && x.IsActive,
                context_.CancellationToken);

        if (membership is null)
        {
            // Bir firmaya bagli olmayan kullanicinin satin almasini sessizce
            // dusurmek parayi alip erisim vermemek olur; goruunur birakiyoruz.
            logger.LogError(
                "Ödeme alındı ama alıcı bir firmaya bağlı değil, abonelik açılamadı. " +
                "OrderId={OrderId}, BuyerId={BuyerId}", message.OrderId, message.BuyerId);
            return;
        }

        var plan = message.SubscriptionPlans[0];

        if (!SubscriptionPlans.IsValid(plan))
        {
            logger.LogError("Tanınmayan plan, abonelik açılmadı. OrderId={OrderId}, Plan={Plan}",
                message.OrderId, plan);
            return;
        }

        var hasActive = await context.Subscriptions
            .AnyAsync(x => x.CompanyId == membership.CompanyId
                           && x.Status == SubscriptionStatuses.Active,
                context_.CancellationToken);

        if (hasActive)
        {
            // Yenileme/yukseltme henuz tanimli degil. Mevcut erisimi bozmamak
            // icin dokunmuyoruz; islem gorunur kalsin diye loglaniyor.
            logger.LogWarning(
                "Firmanın zaten aktif aboneliği var, yeni abonelik açılmadı. " +
                "CompanyId={CompanyId}, OrderId={OrderId}", membership.CompanyId, message.OrderId);
            return;
        }

        var subscription = new Subscription
        {
            Id = NewId.NextSequentialGuid(),
            CompanyId = membership.CompanyId,
            Plan = plan,
            Status = SubscriptionStatuses.Active,
            StartsAt = message.PaidAt,
            OrderId = message.OrderId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = $"order:{message.OrderCode}"
        };

        await context.Subscriptions.AddAsync(subscription, context_.CancellationToken);
        await context.SaveChangesAsync(context_.CancellationToken);

        logger.LogInformation(
            "Satın alma aboneliğe dönüştü. CompanyId={CompanyId}, Plan={Plan}, OrderId={OrderId}",
            membership.CompanyId, plan, message.OrderId);
    }
}

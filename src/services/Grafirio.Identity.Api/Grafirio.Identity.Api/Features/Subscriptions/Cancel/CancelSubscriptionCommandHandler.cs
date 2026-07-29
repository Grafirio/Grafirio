using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Subscriptions.Cancel;

public class CancelSubscriptionCommandHandler(AppDbContext context, IIdentityService identityService)
    : IRequestHandler<CancelSubscriptionCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(CancelSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        if (!identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN))
        {
            return ServiceResult<bool>.Error("Only the platform team can cancel subscriptions",
                HttpStatusCode.Forbidden);
        }

        var subscription = await context.Subscriptions
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

        if (subscription is null)
        {
            return ServiceResult<bool>.Error("Subscription not found", HttpStatusCode.NotFound);
        }

        if (subscription.Status == SubscriptionStatuses.Cancelled)
        {
            // Tekrar iptal, ilk iptalin denetim izini ezmesin.
            return ServiceResult<bool>.SuccessAsOk(true);
        }

        subscription.Status = SubscriptionStatuses.Cancelled;
        subscription.CancelledAt = DateTime.UtcNow;
        subscription.CancelledBy = identityService.UserName;

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

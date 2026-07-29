using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Subscriptions.Create;

public class CreateSubscriptionCommandHandler(AppDbContext context, IIdentityService identityService)
    : IRequestHandler<CreateSubscriptionCommand, ServiceResult<CreateSubscriptionResponse>>
{
    public async Task<ServiceResult<CreateSubscriptionResponse>> Handle(
        CreateSubscriptionCommand request, CancellationToken cancellationToken)
    {
        // Abonelik açmak erişim satmaktır; müşteri kendi kendine açamaz.
        if (!identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN))
        {
            return ServiceResult<CreateSubscriptionResponse>.Error(
                "Only the platform team can create subscriptions", HttpStatusCode.Forbidden);
        }

        if (!SubscriptionPlans.IsValid(request.Plan))
        {
            return ServiceResult<CreateSubscriptionResponse>.Error("Unknown plan",
                $"Plan must be one of: {string.Join(", ", SubscriptionPlans.All)}",
                HttpStatusCode.BadRequest);
        }

        var companyExists = await context.Companies
            .AnyAsync(x => x.Id == request.CompanyId, cancellationToken);

        if (!companyExists)
        {
            return ServiceResult<CreateSubscriptionResponse>.Error("Company not found",
                HttpStatusCode.NotFound);
        }

        var startsAt = request.StartsAt ?? DateTime.UtcNow;

        if (request.EndsAt is not null && request.EndsAt <= startsAt)
        {
            return ServiceResult<CreateSubscriptionResponse>.Error("Invalid subscription period",
                "EndsAt must be later than StartsAt", HttpStatusCode.BadRequest);
        }

        // Ayni firmada ust uste aktif abonelik olusmasin: hangisinin gecerli
        // oldugu belirsizlesir ve erisim kontrolu okunmaz hale gelir.
        var hasActive = await context.Subscriptions
            .AnyAsync(x => x.CompanyId == request.CompanyId
                           && x.Status == SubscriptionStatuses.Active, cancellationToken);

        if (hasActive)
        {
            return ServiceResult<CreateSubscriptionResponse>.Error("Company already has an active subscription",
                "Cancel the existing subscription before creating a new one", HttpStatusCode.Conflict);
        }

        var subscription = new Subscription
        {
            Id = NewId.NextSequentialGuid(),
            CompanyId = request.CompanyId,
            Plan = request.Plan,
            Status = SubscriptionStatuses.Active,
            StartsAt = startsAt,
            EndsAt = request.EndsAt,
            OrderId = request.OrderId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = identityService.UserName
        };

        await context.Subscriptions.AddAsync(subscription, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<CreateSubscriptionResponse>.SuccessAsCreated(
            new CreateSubscriptionResponse(subscription.Id),
            $"/api/v1/subscriptions/{subscription.Id}");
    }
}

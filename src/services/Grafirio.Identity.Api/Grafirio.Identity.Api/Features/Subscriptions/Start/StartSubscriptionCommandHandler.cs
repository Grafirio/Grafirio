using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Subscriptions.Start;

public class StartSubscriptionCommandHandler(AppDbContext context, IIdentityService identityService)
    : IRequestHandler<StartSubscriptionCommand, ServiceResult<StartSubscriptionResponse>>
{
    public async Task<ServiceResult<StartSubscriptionResponse>> Handle(
        StartSubscriptionCommand request, CancellationToken cancellationToken)
    {
        if (!SubscriptionPlans.IsSellable(request.Plan))
        {
            return ServiceResult<StartSubscriptionResponse>.Error("Unknown plan",
                $"Plan must be one of: {string.Join(", ", SubscriptionPlans.Sellable)}",
                HttpStatusCode.BadRequest);
        }

        // UserCompanyRole, Keycloak kimligini metin olarak tutuyor.
        var userId = identityService.UserId.ToString();

        if (string.IsNullOrWhiteSpace(userId) || identityService.UserId == Guid.Empty)
        {
            return ServiceResult<StartSubscriptionResponse>.Error("Unauthenticated",
                HttpStatusCode.Unauthorized);
        }

        // Abonelik firmaya yaziliyor, kisiye degil; dolayisiyla kisinin hangi
        // firma adina konustugu ve o firmada yetkili olup olmadigi burada
        // belirleniyor. Yalnizca yonetici baslatabilir: fatura dogurun bir
        // islem ve siradan bir kullanicinin yapabilecegi sey degil.
        var membership = await context.UserCompanyRoles
            .FirstOrDefaultAsync(x => x.KeycloakUserId == userId
                                      && x.IsActive
                                      && x.Role == CompanyRoles.COMPANY_ADMIN,
                cancellationToken);

        if (membership is null)
        {
            return ServiceResult<StartSubscriptionResponse>.Error(
                "No company to subscribe for",
                "Abonelik başlatmak için bir firmanın yöneticisi olmanız gerekiyor.",
                HttpStatusCode.Forbidden);
        }

        var hasActive = await context.Subscriptions
            .AnyAsync(x => x.CompanyId == membership.CompanyId
                           && x.Status == SubscriptionStatuses.Active, cancellationToken);

        if (hasActive)
        {
            return ServiceResult<StartSubscriptionResponse>.Error(
                "Company already has an active subscription",
                "Firmanızın zaten aktif bir aboneliği var.",
                HttpStatusCode.Conflict);
        }

        var now = DateTime.UtcNow;

        // Deneme yalnizca aylik pakette: kredi paketi on odemeli calisiyor,
        // yuklemeden once zaten bir sey tuketilmiyor.
        DateTime? trialEndsAt = request.Plan == SubscriptionPlans.Monthly
            ? now.AddDays(SubscriptionTrial.Days)
            : null;

        var subscription = new Subscription
        {
            Id = NewId.NextSequentialGuid(),
            CompanyId = membership.CompanyId,
            Plan = request.Plan,
            Status = SubscriptionStatuses.Active,
            StartsAt = now,
            EndsAt = null,
            TrialEndsAt = trialEndsAt,
            CreditBalance = 0m,
            CreatedAt = now,
            CreatedBy = identityService.UserName
        };

        await context.Subscriptions.AddAsync(subscription, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<StartSubscriptionResponse>.SuccessAsCreated(
            new StartSubscriptionResponse(
                subscription.Id,
                subscription.CompanyId,
                subscription.Plan,
                subscription.StartsAt,
                subscription.TrialEndsAt),
            $"/api/v1/subscriptions/{subscription.Id}");
    }
}

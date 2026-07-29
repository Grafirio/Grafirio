using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Grafirio.Identity.Api.Features.Subscriptions.MyAccess;

public class GetMyAccessQueryHandler(AppDbContext context, IIdentityService identityService)
    : IRequestHandler<GetMyAccessQuery, ServiceResult<MyAccessResponse>>
{
    public async Task<ServiceResult<MyAccessResponse>> Handle(GetMyAccessQuery request,
        CancellationToken cancellationToken)
    {
        // Platform ekibi urunu satan taraf; kendi aboneligi olmasi beklenmez.
        if (identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN))
        {
            return ServiceResult<MyAccessResponse>.SuccessAsOk(
                new MyAccessResponse(true, identityService.CurrentCompanyId, null, null,
                    "Platform team"));
        }

        var companyId = identityService.CurrentCompanyId;

        if (companyId is null)
        {
            return ServiceResult<MyAccessResponse>.SuccessAsOk(
                new MyAccessResponse(false, null, null, null, "User is not assigned to a company"));
        }

        var subscriptions = await context.Subscriptions
            .Where(x => x.CompanyId == companyId.Value
                        && x.Status == SubscriptionStatuses.Active)
            .ToListAsync(cancellationToken);

        // Süre kontrolü bellekte: "Active" damgası tek başına yeterli değil,
        // kimse kapatmadığı için süresi dolmuş kayıtlar da bu durumda kalıyor.
        var current = subscriptions.FirstOrDefault(x => x.IsCurrentlyActive());

        if (current is null)
        {
            var reason = subscriptions.Count == 0
                ? "No subscription"
                : "Subscription expired";

            return ServiceResult<MyAccessResponse>.SuccessAsOk(
                new MyAccessResponse(false, companyId, null, null, reason));
        }

        return ServiceResult<MyAccessResponse>.SuccessAsOk(
            new MyAccessResponse(true, companyId, current.Plan, current.EndsAt, "Active subscription"));
    }
}

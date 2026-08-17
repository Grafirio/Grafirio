using Grafirio.Identity.Api.Features.Companies.Access;
using AutoMapper;
using Grafirio.Identity.Api.Features.Subscriptions.Dtos;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Subscriptions.GetByCompany;

public class GetCompanySubscriptionsQueryHandler(
    AppDbContext context,
    ICompanyAccessService access,
    IMapper mapper)
    : IRequestHandler<GetCompanySubscriptionsQuery, ServiceResult<List<SubscriptionDto>>>
{
    public async Task<ServiceResult<List<SubscriptionDto>>> Handle(
        GetCompanySubscriptionsQuery request, CancellationToken cancellationToken)
    {
        // Platform ekibi her firmanın aboneliğini görebilmeli; müşteri yalnızca
        // erişimi olan firmalarınkini.
        if (!await access.CanAccessAsync(request.CompanyId, cancellationToken))
        {
            return ServiceResult<List<SubscriptionDto>>.Error("Access denied to company",
                HttpStatusCode.Forbidden);
        }

        var subscriptions = await context.Subscriptions
            .Where(x => x.CompanyId == request.CompanyId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return ServiceResult<List<SubscriptionDto>>.SuccessAsOk(
            mapper.Map<List<SubscriptionDto>>(subscriptions));
    }
}

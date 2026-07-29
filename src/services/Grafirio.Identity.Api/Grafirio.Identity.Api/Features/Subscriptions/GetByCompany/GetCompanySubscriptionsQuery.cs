using Grafirio.Identity.Api.Features.Subscriptions.Dtos;

namespace Grafirio.Identity.Api.Features.Subscriptions.GetByCompany;

public record GetCompanySubscriptionsQuery(Guid CompanyId)
    : IRequestByServiceResult<List<SubscriptionDto>>;

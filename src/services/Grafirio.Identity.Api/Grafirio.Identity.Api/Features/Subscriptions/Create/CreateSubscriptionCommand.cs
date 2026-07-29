namespace Grafirio.Identity.Api.Features.Subscriptions.Create;

public record CreateSubscriptionCommand(
    Guid CompanyId,
    string Plan,
    DateTime? StartsAt,
    DateTime? EndsAt,
    Guid? OrderId
) : IRequestByServiceResult<CreateSubscriptionResponse>;

public record CreateSubscriptionResponse(Guid Id);

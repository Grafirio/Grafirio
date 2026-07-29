namespace Grafirio.Identity.Api.Features.Subscriptions.Cancel;

public record CancelSubscriptionCommand(Guid Id) : IRequestByServiceResult<bool>;

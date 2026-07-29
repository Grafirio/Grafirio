namespace Grafirio.Identity.Api.Features.Users.RevokeRole;

public record RevokeRoleCommand(string KeycloakUserId, Guid CompanyId)
    : IRequestByServiceResult<bool>;

namespace Grafirio.Identity.Api.Features.Users.AssignRole;

public record AssignRoleCommand(
    string KeycloakUserId,
    Guid CompanyId,
    string Role
) : IRequestByServiceResult<AssignRoleResponse>;

public record AssignRoleResponse(Guid Id);

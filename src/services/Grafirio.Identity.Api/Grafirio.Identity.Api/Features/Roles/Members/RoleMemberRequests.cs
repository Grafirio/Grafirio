using Grafirio.Identity.Api.Features.Roles.Dtos;

namespace Grafirio.Identity.Api.Features.Roles.Members;

public record AssignUserToRoleCommand(Guid RoleId, string KeycloakUserId)
    : IRequestByServiceResult<bool>;

public record RemoveUserFromRoleCommand(Guid RoleId, string KeycloakUserId)
    : IRequestByServiceResult<bool>;

public record GetRoleMembersQuery(Guid RoleId)
    : IRequestByServiceResult<List<RoleMemberDto>>;

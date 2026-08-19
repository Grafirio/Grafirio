using Grafirio.Identity.Api.Features.Departments.Dtos;

namespace Grafirio.Identity.Api.Features.Departments.Members;

public record AssignUserToDepartmentCommand(Guid DepartmentId, string KeycloakUserId)
    : IRequestByServiceResult<bool>;

public record RemoveUserFromDepartmentCommand(Guid DepartmentId, string KeycloakUserId)
    : IRequestByServiceResult<bool>;

public record GetDepartmentMembersQuery(Guid DepartmentId)
    : IRequestByServiceResult<List<DepartmentMemberDto>>;

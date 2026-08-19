using AutoMapper;
using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Roles.Dtos;
using Grafirio.Identity.Api.Features.Users.Directory;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Roles.Members;

public class GetRoleMembersQueryHandler(
    AppDbContext context,
    IPermissionService permissions,
    KeycloakUserDirectory directory,
    IMapper mapper)
    : IRequestHandler<GetRoleMembersQuery, ServiceResult<List<RoleMemberDto>>>
{
    public async Task<ServiceResult<List<RoleMemberDto>>> Handle(GetRoleMembersQuery request,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .FirstOrDefaultAsync(x => x.Id == request.RoleId && x.IsActive, cancellationToken);

        if (role is null)
        {
            return ServiceResult<List<RoleMemberDto>>.Error("Role not found",
                HttpStatusCode.NotFound);
        }

        if (!await permissions.CanAsync(role.CompanyId, AppPermissions.RolesRead, cancellationToken))
        {
            return ServiceResult<List<RoleMemberDto>>.Error("Access denied to module",
                HttpStatusCode.Forbidden);
        }

        var members = await context.UserRoles
            .Where(x => x.RoleId == role.Id && x.IsActive)
            .OrderBy(x => x.AssignedAt)
            .ToListAsync(cancellationToken);

        var result = mapper.Map<List<RoleMemberDto>>(members);

        // Uye listesi de kimlik degil ad gostersin; bkz. KeycloakUserDirectory.
        var people = await directory.LookupAsync(
            result.Select(x => x.KeycloakUserId), cancellationToken);

        foreach (var row in result)
        {
            if (!people.TryGetValue(row.KeycloakUserId, out var person)) continue;

            row.DisplayName = person.DisplayName;
            row.Email = person.Email;
        }

        return ServiceResult<List<RoleMemberDto>>.SuccessAsOk(result);
    }
}

using Grafirio.Identity.Api.Features.Permissions;
using AutoMapper;
using Grafirio.Identity.Api.Features.Users.Directory;
using Grafirio.Identity.Api.Features.Users.Dtos;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Users.GetByCompany;

public class GetCompanyUsersQueryHandler(
    AppDbContext context,
    IPermissionService permissions,
    KeycloakUserDirectory directory,
    IMapper mapper)
    : IRequestHandler<GetCompanyUsersQuery, ServiceResult<List<UserCompanyRoleDto>>>
{
    public async Task<ServiceResult<List<UserCompanyRoleDto>>> Handle(
        GetCompanyUsersQuery request, CancellationToken cancellationToken)
    {
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.UsersRead, cancellationToken))
        {
            return ServiceResult<List<UserCompanyRoleDto>>.Error("Insufficient permissions",
                HttpStatusCode.Forbidden);
        }

        var query = context.UserCompanyRoles.Where(x => x.CompanyId == request.CompanyId);

        if (!request.IncludeRevoked)
        {
            query = query.Where(x => x.IsActive);
        }

        var roles = await query
            .OrderByDescending(x => x.AssignedAt)
            .ToListAsync(cancellationToken);

        var result = mapper.Map<List<UserCompanyRoleDto>>(roles);

        // Liste Keycloak kimligi (GUID) gosteriyordu: ad ve e-posta yetki
        // kaydinda degil Keycloak'ta duruyor. Tek cagrida coz, satir basina
        // ayri istek atmayalim; okunamayan kayit adsiz kaliyor ve panel
        // kimlige dusuyor.
        var people = await directory.LookupAsync(
            result.Select(x => x.KeycloakUserId), cancellationToken);

        foreach (var row in result)
        {
            if (!people.TryGetValue(row.KeycloakUserId, out var person)) continue;

            row.DisplayName = person.DisplayName;
            row.Email = person.Email;
            row.FirstName = person.FirstName;
            row.LastName = person.LastName;
        }

        return ServiceResult<List<UserCompanyRoleDto>>.SuccessAsOk(result);
    }
}

using AutoMapper;
using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Departments.Dtos;
using Grafirio.Identity.Api.Features.Users.Directory;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Departments.Members;

public class GetDepartmentMembersQueryHandler(
    AppDbContext context,
    IPermissionService permissions,
    KeycloakUserDirectory directory,
    IMapper mapper)
    : IRequestHandler<GetDepartmentMembersQuery, ServiceResult<List<DepartmentMemberDto>>>
{
    public async Task<ServiceResult<List<DepartmentMemberDto>>> Handle(GetDepartmentMembersQuery request,
        CancellationToken cancellationToken)
    {
        var department = await context.Departments
            .FirstOrDefaultAsync(x => x.Id == request.DepartmentId && x.IsActive, cancellationToken);

        if (department is null)
        {
            return ServiceResult<List<DepartmentMemberDto>>.Error("Department not found",
                HttpStatusCode.NotFound);
        }

        if (!await permissions.CanAsync(department.CompanyId, AppPermissions.DepartmentsRead, cancellationToken))
        {
            return ServiceResult<List<DepartmentMemberDto>>.Error("Access denied to module",
                HttpStatusCode.Forbidden);
        }

        var members = await context.UserDepartments
            .Where(x => x.DepartmentId == department.Id && x.IsActive)
            .OrderBy(x => x.AssignedAt)
            .ToListAsync(cancellationToken);

        var result = mapper.Map<List<DepartmentMemberDto>>(members);

        // Uye listesi de kimlik degil ad gostersin; bkz. KeycloakUserDirectory.
        var people = await directory.LookupAsync(
            result.Select(x => x.KeycloakUserId), cancellationToken);

        foreach (var row in result)
        {
            if (!people.TryGetValue(row.KeycloakUserId, out var person)) continue;

            row.DisplayName = person.DisplayName;
            row.Email = person.Email;
        }

        return ServiceResult<List<DepartmentMemberDto>>.SuccessAsOk(result);
    }
}

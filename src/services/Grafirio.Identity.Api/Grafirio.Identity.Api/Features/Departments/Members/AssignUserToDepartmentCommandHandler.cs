using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Departments.Members;

public class AssignUserToDepartmentCommandHandler(
    AppDbContext context,
    IPermissionService permissions,
    IIdentityService identityService)
    : IRequestHandler<AssignUserToDepartmentCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(AssignUserToDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        var department = await context.Departments
            .FirstOrDefaultAsync(x => x.Id == request.DepartmentId && x.IsActive, cancellationToken);

        if (department is null)
        {
            return ServiceResult<bool>.Error("Department not found", HttpStatusCode.NotFound);
        }

        if (!await permissions.CanAsync(department.CompanyId, AppPermissions.DepartmentsAssignMembers, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Departmana kullanıcı atamak için üyelik yönetimi izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        // Kullanici once o sirketin uyesi olmali: departman sirket icindeki bir
        // gruplama, disaridan birini departmana koymak sirkete de sessizce
        // sokmak anlamina gelirdi.
        var isCompanyMember = await context.UserCompanyRoles.AnyAsync(
            x => x.KeycloakUserId == request.KeycloakUserId
                 && x.CompanyId == department.CompanyId
                 && x.IsActive,
            cancellationToken);

        if (!isCompanyMember)
        {
            return ServiceResult<bool>.Error("User is not a member of the company",
                "Kullanıcı bu şirketin üyesi değil; önce şirkete eklenmeli.",
                HttpStatusCode.BadRequest);
        }

        var existing = await context.UserDepartments.FirstOrDefaultAsync(
            x => x.DepartmentId == department.Id
                 && x.KeycloakUserId == request.KeycloakUserId
                 && x.IsActive,
            cancellationToken);

        // Ayni atama tekrar gonderildiginde ikinci bir kayit acilmiyor; hangi
        // uyeligin gecerli oldugu belirsizlesirdi.
        if (existing is not null) return ServiceResult<bool>.SuccessAsOk(true);

        await context.UserDepartments.AddAsync(new UserDepartment
        {
            Id = NewId.NextSequentialGuid(),
            KeycloakUserId = request.KeycloakUserId,
            DepartmentId = department.Id,
            CompanyId = department.CompanyId,
            IsActive = true,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = identityService.UserName
        }, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

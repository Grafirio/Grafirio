using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Roles.Members;

public class AssignUserToRoleCommandHandler(
    AppDbContext context,
    IPermissionService permissions,
    IIdentityService identityService)
    : IRequestHandler<AssignUserToRoleCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(AssignUserToRoleCommand request,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .FirstOrDefaultAsync(x => x.Id == request.RoleId && x.IsActive, cancellationToken);

        if (role is null)
        {
            return ServiceResult<bool>.Error("Role not found", HttpStatusCode.NotFound);
        }

        if (!await permissions.CanAsync(role.CompanyId, AppPermissions.RolesAssign, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Rola kullanıcı atamak için üyelik yönetimi izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        // Kullanici once o sirketin uyesi olmali: rol sirket icindeki bir
        // gruplama, disaridan birini rola koymak sirkete de sessizce
        // sokmak anlamina gelirdi.
        var isCompanyMember = await context.CompanyMemberships.AnyAsync(
            x => x.KeycloakUserId == request.KeycloakUserId
                 && x.CompanyId == role.CompanyId
                 && x.IsActive,
            cancellationToken);

        if (!isCompanyMember)
        {
            return ServiceResult<bool>.Error("User is not a member of the company",
                "Kullanıcı bu şirketin üyesi değil; önce şirkete eklenmeli.",
                HttpStatusCode.BadRequest);
        }

        var existing = await context.UserRoles.FirstOrDefaultAsync(
            x => x.RoleId == role.Id
                 && x.KeycloakUserId == request.KeycloakUserId
                 && x.IsActive,
            cancellationToken);

        // Ayni atama tekrar gonderildiginde ikinci bir kayit acilmiyor; hangi
        // uyeligin gecerli oldugu belirsizlesirdi.
        if (existing is not null) return ServiceResult<bool>.SuccessAsOk(true);

        await context.UserRoles.AddAsync(new UserRole
        {
            Id = NewId.NextSequentialGuid(),
            KeycloakUserId = request.KeycloakUserId,
            RoleId = role.Id,
            CompanyId = role.CompanyId,
            IsActive = true,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = identityService.UserName
        }, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Roles.Members;

public class RemoveUserFromRoleCommandHandler(
    AppDbContext context,
    IPermissionService permissions,
    IIdentityService identityService)
    : IRequestHandler<RemoveUserFromRoleCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(RemoveUserFromRoleCommand request,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .FirstOrDefaultAsync(x => x.Id == request.RoleId, cancellationToken);

        if (role is null)
        {
            return ServiceResult<bool>.Error("Role not found", HttpStatusCode.NotFound);
        }

        if (!await permissions.CanAsync(role.CompanyId, AppPermissions.RolesAssign, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Roldan kullanıcı çıkarmak için üyelik yönetimi izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        var membership = await context.UserRoles.FirstOrDefaultAsync(
            x => x.RoleId == role.Id
                 && x.KeycloakUserId == request.KeycloakUserId
                 && x.IsActive,
            cancellationToken);

        if (membership is null)
        {
            return ServiceResult<bool>.Error("Membership not found", HttpStatusCode.NotFound);
        }

        // Silinmiyor, kapatiliyor — kimin ne zaman hangi rolda oldugu
        // sonradan sorulabilsin (CompanyMembership ile ayni desen).
        membership.IsActive = false;
        membership.RemovedAt = DateTime.UtcNow;
        membership.RemovedBy = identityService.UserName;

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

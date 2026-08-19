using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Users.RevokeRole;

public class RevokeRoleCommandHandler(
    AppDbContext context,
    IPermissionService permissions,
    IKeycloakUserService keycloakService,
    IIdentityService identityService)
    : IRequestHandler<RevokeRoleCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(RevokeRoleCommand request,
        CancellationToken cancellationToken)
    {
        // Yetki almak da vermek gibi ayni izne bagli ve hiyerarsik.
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.UsersManageMembership, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Yetki kaldırmak için bu şirkette yetki yönetimi izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        var role = await context.CompanyMemberships
            .FirstOrDefaultAsync(x => x.KeycloakUserId == request.KeycloakUserId
                                      && x.CompanyId == request.CompanyId
                                      && x.IsActive, cancellationToken);

        if (role is null)
        {
            return ServiceResult<bool>.Error("Active role not found", HttpStatusCode.NotFound);
        }

        // Kayit silinmez: kimin ne zaman yetkisi vardi sorusu sonradan
        // cevaplanabilsin diye iz birakilarak kapatilir.
        role.IsActive = false;
        role.RevokedAt = DateTime.UtcNow;
        role.RevokedBy = identityService.UserName;

        await context.SaveChangesAsync(cancellationToken);

        // Token'daki claim'ler de temizlenmezse kullanıcı erişimini korur.
        await keycloakService.RemoveUserFromCompanyAsync(request.KeycloakUserId, request.CompanyId);

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

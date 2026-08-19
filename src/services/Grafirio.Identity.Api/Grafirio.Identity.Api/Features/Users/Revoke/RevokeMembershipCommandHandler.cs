using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Users.Revoke;

public class RevokeMembershipCommandHandler(
    AppDbContext context,
    IPermissionService permissions,
    IKeycloakUserService keycloakService,
    IIdentityService identityService)
    : IRequestHandler<RevokeMembershipCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(RevokeMembershipCommand request,
        CancellationToken cancellationToken)
    {
        // Üyelik kaldırmak da vermek gibi aynı izne bağlı ve hiyerarşik.
        if (!await permissions.CanAsync(request.CompanyId,
                AppPermissions.UsersManageMembership, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Üyelik kaldırmak için bu şirkette üyelik yönetimi izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        var membership = await context.CompanyMemberships
            .FirstOrDefaultAsync(x => x.KeycloakUserId == request.KeycloakUserId
                                      && x.CompanyId == request.CompanyId
                                      && x.IsActive, cancellationToken);

        if (membership is null)
        {
            return ServiceResult<bool>.Error("Active membership not found", HttpStatusCode.NotFound);
        }

        // Kurucunun üyeliği kapatılamaz: kapatılabilseydi bir admin, şirketi
        // kuran kişiyi kendi şirketinden çıkarabilirdi.
        if (membership.Level == MembershipLevels.Founder)
        {
            return ServiceResult<bool>.Error("Founder membership cannot be revoked",
                "Kurucunun üyeliği kaldırılamaz.", HttpStatusCode.Forbidden);
        }

        // Kayıt silinmez: kimin ne zaman üye olduğu sonradan cevaplanabilsin
        // diye iz bırakılarak kapatılır. Rol atamaları da kapanıyor, aksi
        // halde kişi şirkete geri alındığında eski izinleriyle dönerdi.
        membership.IsActive = false;
        membership.RevokedAt = DateTime.UtcNow;
        membership.RevokedBy = identityService.UserName;

        var userRoles = await context.UserRoles
            .Where(x => x.KeycloakUserId == request.KeycloakUserId
                        && x.CompanyId == request.CompanyId && x.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var userRole in userRoles)
        {
            userRole.IsActive = false;
            userRole.RemovedAt = DateTime.UtcNow;
            userRole.RemovedBy = identityService.UserName;
        }

        await context.SaveChangesAsync(cancellationToken);

        // Token'daki claim'ler de temizlenmezse kullanıcı erişimini korur.
        await keycloakService.RemoveUserFromCompanyAsync(request.KeycloakUserId, request.CompanyId);

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

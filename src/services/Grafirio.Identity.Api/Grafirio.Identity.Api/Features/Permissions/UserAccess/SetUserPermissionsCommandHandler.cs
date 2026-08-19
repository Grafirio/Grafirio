using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Grafirio.Shared.Identity.Permissions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Permissions.UserAccess;

public class SetUserPermissionsCommandHandler(
    AppDbContext context,
    IPermissionService permissions,
    IIdentityService identityService)
    : IRequestHandler<SetUserPermissionsCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(SetUserPermissionsCommand request,
        CancellationToken cancellationToken)
    {
        // Kişisel izin vermek, bir rolün izin kümesini düzenlemekle aynı
        // ağırlıkta iş: ikisi de yetkinin kendisini şekillendiriyor ve aynı
        // ekrandan yapılıyor.
        if (!await permissions.CanAsync(request.CompanyId,
                AppPermissions.RolesManagePermissions, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Kişiye özel izin vermek için bu şirkette izin düzenleme yetkiniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        var membership = await context.CompanyMemberships
            .FirstOrDefaultAsync(x => x.KeycloakUserId == request.KeycloakUserId
                                      && x.CompanyId == request.CompanyId
                                      && x.IsActive, cancellationToken);

        if (membership is null)
        {
            return ServiceResult<bool>.Error("Active membership not found",
                "Bu kişi şirketin üyesi değil.", HttpStatusCode.NotFound);
        }

        // Kurucu ve admin izin şemasının dışında. İzin yazmak sessizce etkisiz
        // kalırdı: kaydedildi denip hiçbir şey değişmemesi, ekranda yanlış bir
        // yetki tablosu bırakır.
        if (MembershipLevels.BypassesPermissions(membership.Level))
        {
            return ServiceResult<bool>.Error("Level bypasses permissions",
                "Kurucu ve adminler izin kümesiyle sınırlandırılamaz; önce üyelik seviyesini değiştirin.",
                HttpStatusCode.BadRequest);
        }

        var granted = PermissionSet.Sanitize(request.Permissions);

        var existing = await context.UserPermissions
            .FirstOrDefaultAsync(x => x.KeycloakUserId == request.KeycloakUserId
                                      && x.CompanyId == request.CompanyId, cancellationToken);

        if (existing is null)
        {
            await context.UserPermissions.AddAsync(new UserPermission
            {
                Id = NewId.NextSequentialGuid(),
                KeycloakUserId = request.KeycloakUserId,
                CompanyId = request.CompanyId,
                Permissions = granted,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = identityService.UserName
            }, cancellationToken);
        }
        else
        {
            existing.Permissions = granted;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = identityService.UserName;
        }

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

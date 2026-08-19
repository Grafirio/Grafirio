using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Roles.Delete;

public class DeleteRoleCommandHandler(AppDbContext context, IPermissionService permissions)
    : IRequestHandler<DeleteRoleCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(DeleteRoleCommand request,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.IsActive, cancellationToken);

        if (role is null)
        {
            return ServiceResult<bool>.Error("Role not found", HttpStatusCode.NotFound);
        }

        if (!await permissions.CanAsync(role.CompanyId, AppPermissions.RolesDelete, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Rol silmek için bu şirkette rol silme izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        // Kayit silinmiyor, kapatiliyor: hangi kullanicinin ne zaman hangi
        // rolda oldugu sorusu sonradan cevaplanabilsin. Uyelikler de
        // birlikte kapaniyor, yoksa silinmis bir rola bagli aktif
        // uyelikler kaliyor.
        role.IsActive = false;
        role.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        var memberships = await context.UserRoles
            .Where(x => x.RoleId == role.Id && x.IsActive)
            .ToListAsync(cancellationToken);

        // Uyelikler tek tek kaydediliyor, hepsi birden degil: MongoDB EF
        // saglayicisi cok varlikli SaveChanges'i transaction'a sariyor, calisan
        // MongoDB ise tek dugum ve transaction desteklemiyor ("Standalone
        // servers do not support transactions"). Toplu yazim, iki uyeli bir
        // roli silinemez hale getiriyordu.
        var now = DateTime.UtcNow;
        foreach (var membership in memberships)
        {
            membership.IsActive = false;
            membership.RemovedAt = now;
            membership.RemovedBy = "role-deleted";
            await context.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

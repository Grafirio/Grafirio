using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Roles.Update;

public class UpdateRoleCommandHandler(AppDbContext context, IPermissionService permissions)
    : IRequestHandler<UpdateRoleCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(UpdateRoleCommand request,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.IsActive, cancellationToken);

        if (role is null)
        {
            return ServiceResult<bool>.Error("Role not found", HttpStatusCode.NotFound);
        }

        // Yetki rolin kendi sirketinden sorulur; istekte sirket kimligi
        // tasinmiyor ki cagiran onu degistirerek baska bir sirketin
        // rolina dokunamasin.
        if (!await permissions.CanAsync(role.CompanyId, AppPermissions.RolesUpdate, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Rol düzenlemek için bu şirkette rol düzenleme izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<bool>.Error("Name is required",
                "Rol adı gerekli.", HttpStatusCode.BadRequest);
        }

        var name = request.Name.Trim();

        if (name != role.Name)
        {
            var nameTaken = await context.Roles.AnyAsync(
                x => x.CompanyId == role.CompanyId && x.Name == name
                     && x.Id != role.Id && x.IsActive,
                cancellationToken);

            if (nameTaken)
            {
                return ServiceResult<bool>.Error("Role name already exists",
                    $"'{name}' adında bir rol bu şirkette zaten var.", HttpStatusCode.BadRequest);
            }
        }

        // Taninmayan anahtarlar suzuluyor; bkz. CreateRoleCommandHandler.
        var granted = PermissionSet.Sanitize(request.Permissions);

        // Izin kumesini degistirmek rolu yeniden adlandirmaktan ayri bir yetki:
        // ad ve aciklama neyin ne oldugunu anlatir, izin kumesi ise yetkinin
        // kendisini sekillendirir. Kume degismediginde bu izin sorulmuyor —
        // aksi halde bir yazim hatasini duzeltmek de izin duzenleme yetkisi
        // isterdi.
        var current = role.Permissions.ToHashSet();

        if (!current.SetEquals(granted) &&
            !await permissions.CanAsync(role.CompanyId,
                AppPermissions.RolesManagePermissions, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Rolün izinlerini değiştirmek için izin düzenleme yetkiniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        role.Name = name;
        role.Description = Clean(request.Description);
        role.Permissions = granted;
        role.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.SuccessAsOk(true);
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

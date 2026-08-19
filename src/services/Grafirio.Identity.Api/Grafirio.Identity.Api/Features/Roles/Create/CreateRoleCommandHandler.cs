using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Roles.Create;

public class CreateRoleCommandHandler(AppDbContext context, IPermissionService permissions)
    : IRequestHandler<CreateRoleCommand, ServiceResult<CreateRoleResponse>>
{
    public async Task<ServiceResult<CreateRoleResponse>> Handle(CreateRoleCommand request,
        CancellationToken cancellationToken)
    {
        // Rol kurmak, sirketin yetki semasini sekillendirmenin bir parcasi.
        // Yetki hiyerarsik: kok sirketin admini subelerinde de rol acabilir.
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.RolesCreate, cancellationToken))
        {
            return ServiceResult<CreateRoleResponse>.Error("Insufficient permissions",
                "Rol eklemek için bu şirkette rol ekleme izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<CreateRoleResponse>.Error("Name is required",
                "Rol adı gerekli.", HttpStatusCode.BadRequest);
        }

        var companyExists = await context.Companies
            .AnyAsync(x => x.Id == request.CompanyId && x.IsActive, cancellationToken);

        if (!companyExists)
        {
            return ServiceResult<CreateRoleResponse>.Error("Company not found",
                HttpStatusCode.NotFound);
        }

        var name = request.Name.Trim();

        // Ad sirket icinde benzersiz: ayni adla iki rol, kime hangi izin
        // kumesinin verildigini belirsiz birakirdi. Kardes subede ayni adin
        // bulunmasi normal — izin semasi sirkete bagli.
        var nameTaken = await context.Roles.AnyAsync(
            x => x.CompanyId == request.CompanyId && x.Name == name && x.IsActive,
            cancellationToken);

        if (nameTaken)
        {
            return ServiceResult<CreateRoleResponse>.Error("Role name already exists",
                $"'{name}' adında bir rol bu şirkette zaten var.", HttpStatusCode.BadRequest);
        }

        var granted = PermissionSet.Sanitize(request.Permissions);

        // Izinli bir rol kurmak, kurulmus bir rolin izinlerini degistirmekle
        // ayni agirlikta is (bkz. UpdateRoleCommandHandler): ikisi de yetkiyi
        // sekillendiriyor. Bos bir rol acmak icin bu yetki gerekmiyor.
        if (granted.Count > 0 &&
            !await permissions.CanAsync(request.CompanyId,
                AppPermissions.RolesManagePermissions, cancellationToken))
        {
            return ServiceResult<CreateRoleResponse>.Error("Insufficient permissions",
                "İzin tanımlı bir rol açmak için izin düzenleme yetkiniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        var role = new Role
        {
            Id = NewId.NextSequentialGuid(),
            CompanyId = request.CompanyId,
            Name = name,
            Description = Clean(request.Description),
            // Taninmayan anahtarlar ve PANEL.READ suzuluyor; bkz.
            // PermissionSet.Sanitize.
            Permissions = granted,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await context.Roles.AddAsync(role, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<CreateRoleResponse>.SuccessAsCreated(
            new CreateRoleResponse(role.Id), $"/api/v1/roles/{role.Id}");
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Departments.Delete;

public class DeleteDepartmentCommandHandler(AppDbContext context, IPermissionService permissions)
    : IRequestHandler<DeleteDepartmentCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(DeleteDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        var department = await context.Departments
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.IsActive, cancellationToken);

        if (department is null)
        {
            return ServiceResult<bool>.Error("Department not found", HttpStatusCode.NotFound);
        }

        if (!await permissions.CanAsync(department.CompanyId, AppPermissions.DepartmentsDelete, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Departman silmek için bu şirkette departman silme izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        // Kayit silinmiyor, kapatiliyor: hangi kullanicinin ne zaman hangi
        // departmanda oldugu sorusu sonradan cevaplanabilsin. Uyelikler de
        // birlikte kapaniyor, yoksa silinmis bir departmana bagli aktif
        // uyelikler kaliyor.
        department.IsActive = false;
        department.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        var memberships = await context.UserDepartments
            .Where(x => x.DepartmentId == department.Id && x.IsActive)
            .ToListAsync(cancellationToken);

        // Uyelikler tek tek kaydediliyor, hepsi birden degil: MongoDB EF
        // saglayicisi cok varlikli SaveChanges'i transaction'a sariyor, calisan
        // MongoDB ise tek dugum ve transaction desteklemiyor ("Standalone
        // servers do not support transactions"). Toplu yazim, iki uyeli bir
        // departmani silinemez hale getiriyordu.
        var now = DateTime.UtcNow;
        foreach (var membership in memberships)
        {
            membership.IsActive = false;
            membership.RemovedAt = now;
            membership.RemovedBy = "department-deleted";
            await context.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

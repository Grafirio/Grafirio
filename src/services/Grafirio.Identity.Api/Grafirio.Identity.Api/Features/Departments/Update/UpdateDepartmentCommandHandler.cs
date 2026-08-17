using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Departments.Update;

public class UpdateDepartmentCommandHandler(AppDbContext context, ICompanyAccessService access)
    : IRequestHandler<UpdateDepartmentCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(UpdateDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        var department = await context.Departments
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.IsActive, cancellationToken);

        if (department is null)
        {
            return ServiceResult<bool>.Error("Department not found", HttpStatusCode.NotFound);
        }

        // Yetki departmanin kendi sirketinden sorulur; istekte sirket kimligi
        // tasinmiyor ki cagiran onu degistirerek baska bir sirketin
        // departmanina dokunamasin.
        if (!await access.HasRoleAsync(department.CompanyId, CompanyRoles.COMPANY_ADMIN, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Departman düzenlemek için bu şirkette yönetici olmanız gerekiyor.",
                HttpStatusCode.Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<bool>.Error("Name is required",
                "Departman adı gerekli.", HttpStatusCode.BadRequest);
        }

        var code = Clean(request.Code);

        if (code is not null && code != department.Code)
        {
            var codeTaken = await context.Departments.AnyAsync(
                x => x.CompanyId == department.CompanyId && x.Code == code
                     && x.Id != department.Id && x.IsActive,
                cancellationToken);

            if (codeTaken)
            {
                return ServiceResult<bool>.Error("Department code already exists",
                    $"'{code}' kodu bu şirkette kullanımda.", HttpStatusCode.BadRequest);
            }
        }

        department.Name = request.Name.Trim();
        department.Code = code;
        department.Description = Clean(request.Description);
        department.ManagerKeycloakUserId = Clean(request.ManagerKeycloakUserId);
        department.CostCenter = Clean(request.CostCenter);
        department.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.SuccessAsOk(true);
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

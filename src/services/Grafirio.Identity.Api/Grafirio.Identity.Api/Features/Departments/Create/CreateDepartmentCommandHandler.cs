using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Departments.Create;

public class CreateDepartmentCommandHandler(AppDbContext context, IPermissionService permissions)
    : IRequestHandler<CreateDepartmentCommand, ServiceResult<CreateDepartmentResponse>>
{
    public async Task<ServiceResult<CreateDepartmentResponse>> Handle(CreateDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        // Departman kurmak yonetici isi. Yetki hiyerarsik: kok sirketin
        // yoneticisi subelerinde de departman acabilir.
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.DepartmentsCreate, cancellationToken))
        {
            return ServiceResult<CreateDepartmentResponse>.Error("Insufficient permissions",
                "Departman eklemek için bu şirkette departman ekleme izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<CreateDepartmentResponse>.Error("Name is required",
                "Departman adı gerekli.", HttpStatusCode.BadRequest);
        }

        var companyExists = await context.Companies
            .AnyAsync(x => x.Id == request.CompanyId && x.IsActive, cancellationToken);

        if (!companyExists)
        {
            return ServiceResult<CreateDepartmentResponse>.Error("Company not found",
                HttpStatusCode.NotFound);
        }

        var code = Clean(request.Code);

        // Kod sirket icinde benzersiz: ayni kodla iki departman, dis
        // sistemlerle eslesmeyi belirsiz hale getirirdi. Kardes subede ayni
        // kodun bulunmasi ise normal, bu yuzden kontrol sirkete kapsamli.
        if (code is not null)
        {
            var codeTaken = await context.Departments.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code && x.IsActive,
                cancellationToken);

            if (codeTaken)
            {
                return ServiceResult<CreateDepartmentResponse>.Error("Department code already exists",
                    $"'{code}' kodu bu şirkette kullanımda.", HttpStatusCode.BadRequest);
            }
        }

        var granted = AppPermissions.Sanitize(request.Permissions, request.Modules);

        var department = new Department
        {
            Id = NewId.NextSequentialGuid(),
            CompanyId = request.CompanyId,
            Name = request.Name.Trim(),
            Code = code,
            Description = Clean(request.Description),
            ManagerKeycloakUserId = Clean(request.ManagerKeycloakUserId),
            CostCenter = Clean(request.CostCenter),
            // Taninmayan anahtarlar ve PANEL.READ suzuluyor; bkz.
            // AppPermissions.Sanitize. Modules izinlerden turetiliyor: iki alan
            // birbirinden ayrisirsa panel bir sey gosterir, sunucu baskasini
            // uygular.
            Permissions = granted,
            Modules = AppPermissions.ModulesOf(granted),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await context.Departments.AddAsync(department, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<CreateDepartmentResponse>.SuccessAsCreated(
            new CreateDepartmentResponse(department.Id), $"/api/v1/departments/{department.Id}");
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

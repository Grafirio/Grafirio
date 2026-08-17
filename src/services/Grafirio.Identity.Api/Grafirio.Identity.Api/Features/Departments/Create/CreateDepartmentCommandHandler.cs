using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Departments.Create;

public class CreateDepartmentCommandHandler(AppDbContext context, ICompanyAccessService access)
    : IRequestHandler<CreateDepartmentCommand, ServiceResult<CreateDepartmentResponse>>
{
    public async Task<ServiceResult<CreateDepartmentResponse>> Handle(CreateDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        // Departman kurmak yonetici isi. Yetki hiyerarsik: kok sirketin
        // yoneticisi subelerinde de departman acabilir.
        if (!await access.HasRoleAsync(request.CompanyId, CompanyRoles.COMPANY_ADMIN, cancellationToken))
        {
            return ServiceResult<CreateDepartmentResponse>.Error("Insufficient permissions",
                "Departman eklemek için bu şirkette yönetici olmanız gerekiyor.",
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

        var department = new Department
        {
            Id = NewId.NextSequentialGuid(),
            CompanyId = request.CompanyId,
            Name = request.Name.Trim(),
            Code = code,
            Description = Clean(request.Description),
            ManagerKeycloakUserId = Clean(request.ManagerKeycloakUserId),
            CostCenter = Clean(request.CostCenter),
            // Taninmayan anahtarlar suzuluyor: istemciden gelen serbest metnin
            // izin kumesine sizmasi, ileride o metin bir modul adina
            // donustugunde sessiz bir yetki acilisi olurdu.
            Modules = [.. (request.Modules ?? []).Where(AppModules.IsValid).Distinct()],
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

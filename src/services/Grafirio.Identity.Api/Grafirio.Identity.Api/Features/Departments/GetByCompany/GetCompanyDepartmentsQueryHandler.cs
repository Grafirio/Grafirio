using AutoMapper;
using Grafirio.Identity.Api.Features.Departments.Dtos;
using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Departments.GetByCompany;

public class GetCompanyDepartmentsQueryHandler(
    AppDbContext context,
    IPermissionService permissions,
    IMapper mapper)
    : IRequestHandler<GetCompanyDepartmentsQuery, ServiceResult<List<DepartmentDto>>>
{
    public async Task<ServiceResult<List<DepartmentDto>>> Handle(GetCompanyDepartmentsQuery request,
        CancellationToken cancellationToken)
    {
        // Modul izni sirkete erisimi de kapsiyor: erisimi olmayanin rolu null,
        // rolu null olanin modulu yok. Degistirmek ayrica yonetici isi.
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.DepartmentsRead, cancellationToken))
        {
            return ServiceResult<List<DepartmentDto>>.Error("Access denied to module",
                "Departman modülüne erişiminiz yok.", HttpStatusCode.Forbidden);
        }

        var departments = await context.Departments
            .Where(x => x.CompanyId == request.CompanyId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var result = mapper.Map<List<DepartmentDto>>(departments);

        if (result.Count == 0) return ServiceResult<List<DepartmentDto>>.SuccessAsOk(result);

        // Uye sayilari tek sorguda: departman basina ayri istek listeyi
        // departman sayisi kadar cagriya bolerdi.
        var memberships = await context.UserDepartments
            .Where(x => x.CompanyId == request.CompanyId && x.IsActive)
            .ToListAsync(cancellationToken);

        var counts = memberships
            .GroupBy(x => x.DepartmentId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var department in result)
        {
            department.MemberCount = counts.TryGetValue(department.Id, out var count) ? count : 0;
        }

        return ServiceResult<List<DepartmentDto>>.SuccessAsOk(result);
    }
}

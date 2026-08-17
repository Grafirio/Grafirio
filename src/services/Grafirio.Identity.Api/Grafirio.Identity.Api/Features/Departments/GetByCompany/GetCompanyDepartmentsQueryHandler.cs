using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Departments.Dtos;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Departments.GetByCompany;

public class GetCompanyDepartmentsQueryHandler(
    AppDbContext context,
    ICompanyAccessService access,
    IMapper mapper)
    : IRequestHandler<GetCompanyDepartmentsQuery, ServiceResult<List<DepartmentDto>>>
{
    public async Task<ServiceResult<List<DepartmentDto>>> Handle(GetCompanyDepartmentsQuery request,
        CancellationToken cancellationToken)
    {
        // Gormek icin sirkete erisim yeterli; degistirmek yonetici isi.
        if (!await access.CanAccessAsync(request.CompanyId, cancellationToken))
        {
            return ServiceResult<List<DepartmentDto>>.Error("Access denied to company",
                HttpStatusCode.Forbidden);
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

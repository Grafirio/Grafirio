using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Departments.Dtos;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Departments.Members;

public class GetDepartmentMembersQueryHandler(
    AppDbContext context,
    ICompanyAccessService access,
    IMapper mapper)
    : IRequestHandler<GetDepartmentMembersQuery, ServiceResult<List<DepartmentMemberDto>>>
{
    public async Task<ServiceResult<List<DepartmentMemberDto>>> Handle(GetDepartmentMembersQuery request,
        CancellationToken cancellationToken)
    {
        var department = await context.Departments
            .FirstOrDefaultAsync(x => x.Id == request.DepartmentId && x.IsActive, cancellationToken);

        if (department is null)
        {
            return ServiceResult<List<DepartmentMemberDto>>.Error("Department not found",
                HttpStatusCode.NotFound);
        }

        if (!await access.CanAccessAsync(department.CompanyId, cancellationToken))
        {
            return ServiceResult<List<DepartmentMemberDto>>.Error("Access denied to company",
                HttpStatusCode.Forbidden);
        }

        var members = await context.UserDepartments
            .Where(x => x.DepartmentId == department.Id && x.IsActive)
            .OrderBy(x => x.AssignedAt)
            .ToListAsync(cancellationToken);

        return ServiceResult<List<DepartmentMemberDto>>.SuccessAsOk(
            mapper.Map<List<DepartmentMemberDto>>(members));
    }
}

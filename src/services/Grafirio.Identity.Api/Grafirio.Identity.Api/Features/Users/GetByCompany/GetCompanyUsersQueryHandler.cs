using Grafirio.Identity.Api.Features.Companies.Access;
using AutoMapper;
using Grafirio.Identity.Api.Features.Users.Dtos;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Users.GetByCompany;

public class GetCompanyUsersQueryHandler(
    AppDbContext context,
    ICompanyAccessService access,
    IMapper mapper)
    : IRequestHandler<GetCompanyUsersQuery, ServiceResult<List<UserCompanyRoleDto>>>
{
    public async Task<ServiceResult<List<UserCompanyRoleDto>>> Handle(
        GetCompanyUsersQuery request, CancellationToken cancellationToken)
    {
        if (!await access.CanAccessAsync(request.CompanyId, cancellationToken))
        {
            return ServiceResult<List<UserCompanyRoleDto>>.Error("Access denied to company",
                HttpStatusCode.Forbidden);
        }

        var query = context.UserCompanyRoles.Where(x => x.CompanyId == request.CompanyId);

        if (!request.IncludeRevoked)
        {
            query = query.Where(x => x.IsActive);
        }

        var roles = await query
            .OrderByDescending(x => x.AssignedAt)
            .ToListAsync(cancellationToken);

        return ServiceResult<List<UserCompanyRoleDto>>.SuccessAsOk(
            mapper.Map<List<UserCompanyRoleDto>>(roles));
    }
}

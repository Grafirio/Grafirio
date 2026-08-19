using AutoMapper;
using Grafirio.Identity.Api.Features.Roles.Dtos;
using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Roles.GetByCompany;

public class GetCompanyRolesQueryHandler(
    AppDbContext context,
    IPermissionService permissions,
    IMapper mapper)
    : IRequestHandler<GetCompanyRolesQuery, ServiceResult<List<RoleDto>>>
{
    public async Task<ServiceResult<List<RoleDto>>> Handle(GetCompanyRolesQuery request,
        CancellationToken cancellationToken)
    {
        // Modul izni sirkete erisimi de kapsiyor: erisimi olmayanin rolu null,
        // rolu null olanin modulu yok. Degistirmek ayrica yonetici isi.
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.RolesRead, cancellationToken))
        {
            return ServiceResult<List<RoleDto>>.Error("Access denied to module",
                "Rol modülüne erişiminiz yok.", HttpStatusCode.Forbidden);
        }

        var roles = await context.Roles
            .Where(x => x.CompanyId == request.CompanyId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var result = mapper.Map<List<RoleDto>>(roles);

        if (result.Count == 0) return ServiceResult<List<RoleDto>>.SuccessAsOk(result);

        // Uye sayilari tek sorguda: rol basina ayri istek listeyi
        // rol sayisi kadar cagriya bolerdi.
        var memberships = await context.UserRoles
            .Where(x => x.CompanyId == request.CompanyId && x.IsActive)
            .ToListAsync(cancellationToken);

        var counts = memberships
            .GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var role in result)
        {
            role.MemberCount = counts.TryGetValue(role.Id, out var count) ? count : 0;
        }

        return ServiceResult<List<RoleDto>>.SuccessAsOk(result);
    }
}

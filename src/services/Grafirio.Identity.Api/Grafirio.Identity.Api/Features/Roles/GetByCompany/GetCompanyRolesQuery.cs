using Grafirio.Identity.Api.Features.Roles.Dtos;

namespace Grafirio.Identity.Api.Features.Roles.GetByCompany;

public record GetCompanyRolesQuery(Guid CompanyId) : IRequestByServiceResult<List<RoleDto>>;

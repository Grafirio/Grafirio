using Grafirio.Identity.Api.Features.Departments.Dtos;

namespace Grafirio.Identity.Api.Features.Departments.GetByCompany;

public record GetCompanyDepartmentsQuery(Guid CompanyId) : IRequestByServiceResult<List<DepartmentDto>>;

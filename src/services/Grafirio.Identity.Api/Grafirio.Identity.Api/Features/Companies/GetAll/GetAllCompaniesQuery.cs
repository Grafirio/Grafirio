using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.GetAll;

public record GetAllCompaniesQuery : IRequestByServiceResult<List<CompanyDto>>;
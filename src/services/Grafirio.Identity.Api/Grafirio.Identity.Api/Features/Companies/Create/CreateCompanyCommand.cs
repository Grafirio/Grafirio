namespace Grafirio.Identity.Api.Features.Companies.Create;

public record CreateCompanyCommand(
    string Name,
    string? Code,
    string? Description,
    Guid? ParentCompanyId
) : IRequestByServiceResult<CreateCompanyResponse>;
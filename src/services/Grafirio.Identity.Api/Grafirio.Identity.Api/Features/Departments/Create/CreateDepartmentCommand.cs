namespace Grafirio.Identity.Api.Features.Departments.Create;

public record CreateDepartmentCommand(
    Guid CompanyId,
    string Name,
    string? Code,
    string? Description,
    string? ManagerKeycloakUserId,
    string? CostCenter,
    List<string>? Modules
) : IRequestByServiceResult<CreateDepartmentResponse>;

public record CreateDepartmentResponse(Guid Id);

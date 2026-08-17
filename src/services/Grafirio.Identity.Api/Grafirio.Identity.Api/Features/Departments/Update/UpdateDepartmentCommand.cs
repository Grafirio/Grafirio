namespace Grafirio.Identity.Api.Features.Departments.Update;

public record UpdateDepartmentCommand(
    Guid Id,
    string Name,
    string? Code,
    string? Description,
    string? ManagerKeycloakUserId,
    string? CostCenter,
    List<string>? Modules
) : IRequestByServiceResult<bool>;

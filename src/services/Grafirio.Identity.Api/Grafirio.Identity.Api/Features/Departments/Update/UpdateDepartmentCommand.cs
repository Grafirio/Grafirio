namespace Grafirio.Identity.Api.Features.Departments.Update;

public record UpdateDepartmentCommand(
    Guid Id,
    string Name,
    string? Code,
    string? Description,
    string? ManagerKeycloakUserId,
    string? CostCenter,
    List<string>? Modules,
    /// İzin anahtarları (MODÜL.AKSİYON). Boş gelirse <paramref name="Modules"/>
    /// izne çevriliyor — güncellenmemiş panel departmanı izinsiz bırakmasın.
    List<string>? Permissions
) : IRequestByServiceResult<bool>;

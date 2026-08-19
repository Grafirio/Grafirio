namespace Grafirio.Identity.Api.Features.Departments.Create;

public record CreateDepartmentCommand(
    Guid CompanyId,
    string Name,
    string? Code,
    string? Description,
    string? ManagerKeycloakUserId,
    string? CostCenter,
    List<string>? Modules,
    /// İzin anahtarları (MODÜL.AKSİYON). Boş gelirse <paramref name="Modules"/>
    /// izne çevriliyor — güncellenmemiş panel departmanı izinsiz bırakmasın.
    List<string>? Permissions
) : IRequestByServiceResult<CreateDepartmentResponse>;

public record CreateDepartmentResponse(Guid Id);

namespace Grafirio.Identity.Api.Features.Roles.Create;

public record CreateRoleCommand(
    Guid CompanyId,
    string Name,
    string? Description,
    /// İzin anahtarları (MODÜL.AKSİYON). Boş liste geçerli: izinsiz bir rol,
    /// henüz doldurulmamış bir tanım demek.
    List<string>? Permissions
) : IRequestByServiceResult<CreateRoleResponse>;

public record CreateRoleResponse(Guid Id);

namespace Grafirio.Identity.Api.Features.Roles.Update;

public record UpdateRoleCommand(
    Guid Id,
    string Name,
    string? Description,
    /// İzin anahtarları (MODÜL.AKSİYON).
    List<string>? Permissions
) : IRequestByServiceResult<bool>;

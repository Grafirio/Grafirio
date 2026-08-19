namespace Grafirio.Identity.Api.Features.Roles.Delete;

public record DeleteRoleCommand(Guid Id) : IRequestByServiceResult<bool>;

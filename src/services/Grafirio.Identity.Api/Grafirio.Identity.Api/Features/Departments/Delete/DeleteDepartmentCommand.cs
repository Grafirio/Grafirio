namespace Grafirio.Identity.Api.Features.Departments.Delete;

public record DeleteDepartmentCommand(Guid Id) : IRequestByServiceResult<bool>;

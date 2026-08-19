namespace Grafirio.Identity.Api.Features.Users.Register;

public record RegisterUserCommand(
    string Email,
    string FirstName,
    string LastName,
    string Password,
    Guid CompanyId
) : IRequestByServiceResult<RegisterUserResponse>;
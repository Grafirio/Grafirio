using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Users.Register;

public class RegisterUserCommandHandler(
    AppDbContext context,
    IPermissionService permissions,
    IKeycloakUserService keycloakService,
    IIdentityService identityService)
    : IRequestHandler<RegisterUserCommand, ServiceResult<RegisterUserResponse>>
{
    public async Task<ServiceResult<RegisterUserResponse>> Handle(RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        // Validate role
        if (!CompanyRoles.IsValid(request.Role))
        {
            return ServiceResult<RegisterUserResponse>.Error("Invalid role",
                $"Role must be one of: {string.Join(", ", CompanyRoles.All)}", HttpStatusCode.BadRequest);
        }

        // Check if company exists and user has access
        var company = await context.Companies
            .FirstOrDefaultAsync(x => x.Id == request.CompanyId && x.IsActive, cancellationToken);

        if (company == null)
        {
            return ServiceResult<RegisterUserResponse>.Error("Company not found",
                HttpStatusCode.NotFound);
        }

        // Kullanici acmak bir izin, rol degil: kural artik AppPermissions'ta
        // duruyor ve departman daraltmasi da hesaba katiliyor. Yetki hiyerarsik
        // oldugu icin kok sirketin yoneticisi subelerinde de kullanici acabilir;
        // platform ekibine servis zaten her zaman izin veriyor.
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.UsersCreate, cancellationToken))
        {
            return ServiceResult<RegisterUserResponse>.Error("Insufficient permissions",
                "Kullanıcı eklemek için bu şirkette kullanıcı ekleme yetkiniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        // Create user in Keycloak
        var createRequest = new CreateUserRequest
        {
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Password = request.Password,
            CompanyId = request.CompanyId,
            Role = request.Role,
            EmailVerified = false,
            RequirePasswordChange = true
        };

        var keycloakResult = await keycloakService.CreateUserAsync(createRequest);
        if (keycloakResult.IsFail)
        {
            return ServiceResult<RegisterUserResponse>.Error("User registration failed",
                keycloakResult.Fail?.Detail ?? "Keycloak registration failed", HttpStatusCode.BadRequest);
        }

        // Create user-company role mapping in our database
        var userRole = new UserCompanyRole
        {
            Id = NewId.NextSequentialGuid(),
            KeycloakUserId = keycloakResult.Data!,
            CompanyId = request.CompanyId,
            Role = request.Role,
            IsActive = true,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = identityService.UserId.ToString()
        };

        context.UserCompanyRoles.Add(userRole);
        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<RegisterUserResponse>.SuccessAsCreated(
            new RegisterUserResponse(keycloakResult.Data!, request.Email),
            $"/api/v1/users/{keycloakResult.Data}");
    }
}
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Users.Register;

public class RegisterUserCommandHandler(
    AppDbContext context,
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

        // Platform ekibi musteri adina kullanici acabilmeli; kendi
        // accessible_companies listesinde olmayan firmalar da dahil.
        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);

        if (!isPlatformAdmin)
        {
            // Check if current user has access to this company
            if (!identityService.HasCompanyAccess(request.CompanyId))
            {
                return ServiceResult<RegisterUserResponse>.Error("Access denied to company",
                    HttpStatusCode.Forbidden);
            }

            // Only admins and managers can register users
            if (!identityService.HasBusinessRole(CompanyRoles.COMPANY_ADMIN, request.CompanyId) &&
                !identityService.HasBusinessRole(CompanyRoles.COMPANY_MANAGER, request.CompanyId))
            {
                return ServiceResult<RegisterUserResponse>.Error("Insufficient permissions",
                    "Only company admins and managers can register users", HttpStatusCode.Forbidden);
            }
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
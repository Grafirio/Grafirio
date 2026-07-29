using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Users.AssignRole;

public class AssignRoleCommandHandler(
    AppDbContext context,
    IKeycloakUserService keycloakService,
    IIdentityService identityService)
    : IRequestHandler<AssignRoleCommand, ServiceResult<AssignRoleResponse>>
{
    public async Task<ServiceResult<AssignRoleResponse>> Handle(AssignRoleCommand request,
        CancellationToken cancellationToken)
    {
        if (!CompanyRoles.IsValid(request.Role))
        {
            return ServiceResult<AssignRoleResponse>.Error("Invalid role",
                $"Role must be one of: {string.Join(", ", CompanyRoles.All)}",
                HttpStatusCode.BadRequest);
        }

        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);

        var companyExists = await context.Companies
            .AnyAsync(x => x.Id == request.CompanyId && x.IsActive, cancellationToken);

        if (!companyExists)
        {
            return ServiceResult<AssignRoleResponse>.Error("Company not found", HttpStatusCode.NotFound);
        }

        if (!isPlatformAdmin)
        {
            if (!identityService.HasCompanyAccess(request.CompanyId))
            {
                return ServiceResult<AssignRoleResponse>.Error("Access denied to company",
                    HttpStatusCode.Forbidden);
            }

            // Yetki dagitmak yonetici isi; sıradan kullanıcı kendi firmasında
            // bile rol atayamaz.
            if (!identityService.HasBusinessRole(CompanyRoles.COMPANY_ADMIN, request.CompanyId))
            {
                return ServiceResult<AssignRoleResponse>.Error("Insufficient permissions",
                    "Only company admins can assign roles", HttpStatusCode.Forbidden);
            }
        }

        var existing = await context.UserCompanyRoles
            .FirstOrDefaultAsync(x => x.KeycloakUserId == request.KeycloakUserId
                                      && x.CompanyId == request.CompanyId
                                      && x.IsActive, cancellationToken);

        // Ayni kisiye ayni firmada ikinci bir aktif rol acmak yerine mevcut
        // kaydi guncelle; aksi halde hangi rolun gecerli oldugu belirsizlesir.
        if (existing is not null)
        {
            if (existing.Role == request.Role)
            {
                return ServiceResult<AssignRoleResponse>.SuccessAsOk(new AssignRoleResponse(existing.Id));
            }

            existing.Role = request.Role;
            existing.AssignedAt = DateTime.UtcNow;
            existing.AssignedBy = identityService.UserName;

            await context.SaveChangesAsync(cancellationToken);
            await SyncKeycloakAsync(request);

            return ServiceResult<AssignRoleResponse>.SuccessAsOk(new AssignRoleResponse(existing.Id));
        }

        var role = new UserCompanyRole
        {
            Id = NewId.NextSequentialGuid(),
            KeycloakUserId = request.KeycloakUserId,
            CompanyId = request.CompanyId,
            Role = request.Role,
            IsActive = true,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = identityService.UserName
        };

        await context.UserCompanyRoles.AddAsync(role, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await SyncKeycloakAsync(request);

        return ServiceResult<AssignRoleResponse>.SuccessAsCreated(
            new AssignRoleResponse(role.Id), $"/api/v1/users/{request.KeycloakUserId}/roles");
    }

    /// <summary>
    /// Yetki kararı burada saklanır ama uygulanması token'daki claim'lere bağlı;
    /// Keycloak tarafındaki company_id / business_roles öznitelikleri de
    /// güncellenmezse kullanıcı yeni rolünü hiç görmez.
    /// </summary>
    private async Task SyncKeycloakAsync(AssignRoleCommand request)
    {
        await keycloakService.AssignUserToCompanyAsync(
            request.KeycloakUserId, request.CompanyId, request.Role);
    }
}

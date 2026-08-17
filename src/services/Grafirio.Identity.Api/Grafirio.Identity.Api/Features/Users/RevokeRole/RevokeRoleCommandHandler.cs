using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Users.RevokeRole;

public class RevokeRoleCommandHandler(
    AppDbContext context,
    ICompanyAccessService access,
    IKeycloakUserService keycloakService,
    IIdentityService identityService)
    : IRequestHandler<RevokeRoleCommand, ServiceResult<bool>>
{
    public async Task<ServiceResult<bool>> Handle(RevokeRoleCommand request,
        CancellationToken cancellationToken)
    {
        // Yetki almak da vermek gibi yonetici isi ve hiyerarsik.
        if (!await access.HasRoleAsync(request.CompanyId, CompanyRoles.COMPANY_ADMIN, cancellationToken))
        {
            return ServiceResult<bool>.Error("Insufficient permissions",
                "Yetki kaldırmak için bu şirkette yönetici olmanız gerekiyor.", HttpStatusCode.Forbidden);
        }

        var role = await context.UserCompanyRoles
            .FirstOrDefaultAsync(x => x.KeycloakUserId == request.KeycloakUserId
                                      && x.CompanyId == request.CompanyId
                                      && x.IsActive, cancellationToken);

        if (role is null)
        {
            return ServiceResult<bool>.Error("Active role not found", HttpStatusCode.NotFound);
        }

        // Kayit silinmez: kimin ne zaman yetkisi vardi sorusu sonradan
        // cevaplanabilsin diye iz birakilarak kapatilir.
        role.IsActive = false;
        role.RevokedAt = DateTime.UtcNow;
        role.RevokedBy = identityService.UserName;

        await context.SaveChangesAsync(cancellationToken);

        // Token'daki claim'ler de temizlenmezse kullanıcı erişimini korur.
        await keycloakService.RemoveUserFromCompanyAsync(request.KeycloakUserId, request.CompanyId);

        return ServiceResult<bool>.SuccessAsOk(true);
    }
}

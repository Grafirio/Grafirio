using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Grafirio.Shared.Identity.Permissions;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Permissions.UserAccess;

public class GetUserAccessQueryHandler(
    AppDbContext context,
    IPermissionService permissions)
    : IRequestHandler<GetUserAccessQuery, ServiceResult<UserAccessDto>>
{
    public async Task<ServiceResult<UserAccessDto>> Handle(GetUserAccessQuery request,
        CancellationToken cancellationToken)
    {
        // Başkasının yetkisini görmek, kullanıcı listesini görmekle aynı kapı:
        // ekranda kimin ne yapabildiği zaten o listenin yanında duruyor.
        if (!await permissions.CanAsync(request.CompanyId, AppPermissions.UsersRead, cancellationToken))
        {
            return ServiceResult<UserAccessDto>.Error("Insufficient permissions",
                "Kullanıcı yetkilerini görmek için bu şirkette kullanıcı görme izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        var membership = await context.CompanyMemberships
            .FirstOrDefaultAsync(x => x.KeycloakUserId == request.KeycloakUserId
                                      && x.CompanyId == request.CompanyId
                                      && x.IsActive, cancellationToken);

        var result = new UserAccessDto
        {
            CompanyId = request.CompanyId,
            KeycloakUserId = request.KeycloakUserId,
            Level = membership?.Level,
            BypassesPermissions = MembershipLevels.BypassesPermissions(membership?.Level)
        };

        if (membership is null) return ServiceResult<UserAccessDto>.SuccessAsOk(result);

        // Kurucu ve admin şemanın dışında: rolleri okumanın anlamı yok, hepsi
        // zaten açık. Rol listesini yine de boş bırakıyoruz ki panel "bu kişi
        // şu rollerde" diye yanıltıcı bir şey göstermesin.
        if (result.BypassesPermissions)
        {
            result.EffectivePermissions = [.. AppPermissions.All];
            return ServiceResult<UserAccessDto>.SuccessAsOk(result);
        }

        var roleIds = await context.UserRoles
            .Where(x => x.KeycloakUserId == request.KeycloakUserId
                        && x.CompanyId == request.CompanyId && x.IsActive)
            .Select(x => x.RoleId)
            .ToListAsync(cancellationToken);

        if (roleIds.Count > 0)
        {
            var roles = await context.Roles
                .Where(x => roleIds.Contains(x.Id) && x.IsActive)
                .ToListAsync(cancellationToken);

            result.Roles = [.. roles
                .OrderBy(r => r.Name)
                .Select(r => new UserAccessRoleDto(r.Id, r.Name, r.Permissions))];
        }

        var personal = await context.UserPermissions
            .FirstOrDefaultAsync(x => x.KeycloakUserId == request.KeycloakUserId
                                      && x.CompanyId == request.CompanyId, cancellationToken);

        result.PersonalPermissions = personal?.Permissions ?? [];

        var effective = new HashSet<string>(result.Roles.SelectMany(r => r.Permissions));
        foreach (var permission in result.PersonalPermissions) effective.Add(permission);
        effective.Add(AppPermissions.PanelRead);

        result.EffectivePermissions = [.. effective.OrderBy(p => p)];

        return ServiceResult<UserAccessDto>.SuccessAsOk(result);
    }
}

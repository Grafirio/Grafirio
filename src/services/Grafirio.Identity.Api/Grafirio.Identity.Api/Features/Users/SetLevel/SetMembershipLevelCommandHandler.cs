using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Users.SetLevel;

public class SetMembershipLevelCommandHandler(
    AppDbContext context,
    IPermissionService permissions,
    IKeycloakUserService keycloakService,
    IIdentityService identityService)
    : IRequestHandler<SetMembershipLevelCommand, ServiceResult<SetMembershipLevelResponse>>
{
    public async Task<ServiceResult<SetMembershipLevelResponse>> Handle(
        SetMembershipLevelCommand request, CancellationToken cancellationToken)
    {
        // Kurucu bu uçtan verilemiyor: şirketi kuran e-postaya bağlı ve devri
        // ayrı bir akış. Aksi halde "kurucu tektir" kuralı bir istekle
        // bozulabilirdi.
        if (request.Level != MembershipLevels.Admin && request.Level != MembershipLevels.Member)
        {
            return ServiceResult<SetMembershipLevelResponse>.Error("Invalid level",
                $"Seviye şu ikisinden biri olmalı: {MembershipLevels.Admin}, {MembershipLevels.Member}.",
                HttpStatusCode.BadRequest);
        }

        var companyExists = await context.Companies
            .AnyAsync(x => x.Id == request.CompanyId && x.IsActive, cancellationToken);

        if (!companyExists)
        {
            return ServiceResult<SetMembershipLevelResponse>.Error("Company not found",
                HttpStatusCode.NotFound);
        }

        // Birini admin yapmak, onu izin şemasının tamamen dışına çıkarmak
        // demek; kullanıcı açmaktan ayrı bir yetki. Hiyerarşik: kök şirketin
        // admini şubelerinde de seviye belirleyebilir.
        if (!await permissions.CanAsync(request.CompanyId,
                AppPermissions.UsersManageMembership, cancellationToken))
        {
            return ServiceResult<SetMembershipLevelResponse>.Error("Insufficient permissions",
                "Üyelik seviyesi değiştirmek için bu şirkette üyelik yönetimi izniniz olmalı.",
                HttpStatusCode.Forbidden);
        }

        var existing = await context.CompanyMemberships
            .FirstOrDefaultAsync(x => x.KeycloakUserId == request.KeycloakUserId
                                      && x.CompanyId == request.CompanyId
                                      && x.IsActive, cancellationToken);

        // Kurucunun seviyesi düşürülemez. Şirketin kendini kilitlemesine karşı
        // son güvence bu: kurucu her zaman izin şemasının dışında kalır ve
        // yanlış tanımlanmış bir rol kümesi onu dışarıda bırakamaz.
        if (existing is not null && existing.Level == MembershipLevels.Founder)
        {
            return ServiceResult<SetMembershipLevelResponse>.Error("Founder cannot be demoted",
                "Kurucunun üyelik seviyesi değiştirilemez.", HttpStatusCode.Forbidden);
        }

        // Aynı kişiye aynı firmada ikinci bir aktif üyelik açmak yerine mevcut
        // kaydı güncelle; aksi halde hangisinin geçerli olduğu belirsizleşir.
        if (existing is not null)
        {
            if (existing.Level == request.Level)
            {
                return ServiceResult<SetMembershipLevelResponse>.SuccessAsOk(
                    new SetMembershipLevelResponse(existing.Id));
            }

            existing.Level = request.Level;
            existing.AssignedAt = DateTime.UtcNow;
            existing.AssignedBy = identityService.UserName;

            await context.SaveChangesAsync(cancellationToken);
            await SyncKeycloakAsync(request);

            return ServiceResult<SetMembershipLevelResponse>.SuccessAsOk(
                new SetMembershipLevelResponse(existing.Id));
        }

        var membership = new CompanyMembership
        {
            Id = NewId.NextSequentialGuid(),
            KeycloakUserId = request.KeycloakUserId,
            CompanyId = request.CompanyId,
            Level = request.Level,
            IsActive = true,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = identityService.UserName
        };

        await context.CompanyMemberships.AddAsync(membership, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await SyncKeycloakAsync(request);

        return ServiceResult<SetMembershipLevelResponse>.SuccessAsCreated(
            new SetMembershipLevelResponse(membership.Id),
            $"/api/v1/users/{request.KeycloakUserId}/membership");
    }

    /// <summary>
    /// Üyelik kararı burada saklanır ama şirket seçimi token'daki company_id
    /// claim'ine bakıyor; Keycloak tarafı güncellenmezse kullanıcı yeni
    /// şirketini hiç görmez.
    /// </summary>
    private async Task SyncKeycloakAsync(SetMembershipLevelCommand request)
    {
        await keycloakService.AssignUserToCompanyAsync(
            request.KeycloakUserId, request.CompanyId, request.Level);
    }
}

using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Companies.Create;

public class CreateCompanyCommandHandler(
    AppDbContext context,
    IKeycloakUserService keycloakService,
    IIdentityService identityService,
    IPermissionService permissions)
    : IRequestHandler<CreateCompanyCommand, ServiceResult<CreateCompanyResponse>>
{
    public async Task<ServiceResult<CreateCompanyResponse>> Handle(CreateCompanyCommand request,
        CancellationToken cancellationToken)
    {
        if (identityService.UserId == Guid.Empty)
        {
            return ServiceResult<CreateCompanyResponse>.Error("Unauthenticated", HttpStatusCode.Unauthorized);
        }

        if (!string.IsNullOrEmpty(request.Code))
        {
            var existingCompany = await context.Companies
                .AnyAsync(x => x.Code == request.Code, cancellationToken);

            if (existingCompany)
            {
                return ServiceResult<CreateCompanyResponse>.Error("Company code already exists",
                    $"'{request.Code}' kodu kullanımda.", HttpStatusCode.BadRequest);
            }
        }

        // Platform ekibi musteri firmalarini kurdugu icin kiracı kapsamının
        // disindadir; kendi uyeligi olmayan bir firmanin altina da sirket
        // acabilmelidir.
        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);
        var userId = identityService.UserId.ToString();

        int level = 0;
        List<Guid> parentPath = [];

        if (request.ParentCompanyId.HasValue)
        {
            var parentCompany = await context.Companies
                .FirstOrDefaultAsync(x => x.Id == request.ParentCompanyId.Value, cancellationToken);

            if (parentCompany == null)
            {
                return ServiceResult<CreateCompanyResponse>.Error("Parent company not found",
                    HttpStatusCode.NotFound);
            }

            // Yetki, token'daki accessible_companies claim'inden degil uyelik
            // kayitlarindan okunuyor; ustelik hiyerarsik, yani kok sirketin
            // yoneticisi herhangi bir subenin altina da sube acabilir.
            //
            // Alt sirket acmak ayri bir izin, sirket bilgisini duzeltmekle ayni
            // sey degil: yeni bir tuzel kisilik dogurur ve altina kullanici,
            // departman, abonelik baglanir.
            if (!await permissions.CanAsync(request.ParentCompanyId.Value,
                    AppPermissions.CompanySettingsCreateChild, cancellationToken))
            {
                return ServiceResult<CreateCompanyResponse>.Error("Access denied to parent company",
                    "Alt şirket açmak için üst şirkette alt şirket açma izniniz olmalı.",
                    HttpStatusCode.Forbidden);
            }

            level = parentCompany.Level + 1;
            parentPath = parentCompany.Path.Count > 0 ? parentCompany.Path : [parentCompany.Id];
        }
        else
        {
            // Kok sirket acmak yeni bir kiraci acmak demek: cagiranin halihazirda
            // bir sirkette kurucu ya da admin olmasi gerekiyor.
            //
            // Kaynak token degil veritabani. Onceden business_roles claim'ine
            // bakiliyordu ve Faz 7'den sonra bu yanlis cevap veriyordu: uyelik
            // seviyesi artik veritabaninda ve kurucunun claim'inde "ADMIN"
            // yazmiyor, dolayisiyla sirketini kuran kisi ikinci bir kok sirket
            // acamiyordu. Yetkinin claim'den veritabanina tasinmasinin sebebi
            // tam olarak buydu (bkz. Faz 3).
            var canOpenTenant = await context.CompanyMemberships.AnyAsync(
                x => x.KeycloakUserId == userId
                     && x.IsActive
                     && (x.Level == MembershipLevels.Founder || x.Level == MembershipLevels.Admin),
                cancellationToken);

            if (!isPlatformAdmin && !canOpenTenant)
            {
                return ServiceResult<CreateCompanyResponse>.Error("Only company admins can create root companies",
                    "Kök şirket açmak için bir şirkette kurucu ya da admin olmanız gerekiyor.",
                    HttpStatusCode.Forbidden);
            }
        }

        var now = DateTime.UtcNow;
        var companyId = NewId.NextSequentialGuid();

        var company = new Company
        {
            Id = companyId,
            Name = request.Name,
            Code = request.Code,
            Description = request.Description,
            ParentCompanyId = request.ParentCompanyId,
            // Yetki hiyerarsik oldugu icin zincir kayit anında yaziliyor;
            // sonradan hesaplanmasi her erisim kontrolunde agaci tirmanmak
            // demek olurdu.
            Path = [.. parentPath, companyId],
            Level = level,
            IsActive = true,
            CreatedAt = now
        };

        await context.Companies.AddAsync(company, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // Kuran kisi yeni sirkette de yonetici olur. Bu adim olmadan sirket
        // kaydi olusuyor ama kurucusunun ona uyeligi bulunmuyordu: firma
        // accessible_companies'e hic girmedigi icin listelerden suzuluyor ve
        // "alt sirket eklenmiyor" gibi gorunuyordu. Sonradan eklenen
        // kullanicilarin yetkisi ayri bir is; burada yalnizca kurucu aliniyor.
        // Sirketi kuran kisi kurucu: izin semasinin disinda, daraltilamaz ve
        // (bugun) devredilemez. Sirketin kendini kilitlemesine karsi son guvence.
        var membership = new CompanyMembership
        {
            Id = NewId.NextSequentialGuid(),
            KeycloakUserId = userId,
            CompanyId = company.Id,
            Level = MembershipLevels.Founder,
            IsActive = true,
            AssignedAt = now,
            AssignedBy = identityService.UserName
        };

        // Iki varlik tek SaveChanges ile yazilamiyor: MongoDB EF saglayicisi
        // cok varlikli kaydetmeyi transaction'a sariyor, calisan MongoDB ise
        // tek dugum ve transaction desteklemiyor. Geri alma bu yuzden elle.
        try
        {
            await context.CompanyMemberships.AddAsync(membership, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Yoneticisi olmayan alt sirket kimsenin erisemedigi bir kayit olur;
            // yarim birakmaktansa sirketi de geri aliyoruz.
            context.Companies.Remove(company);
            await context.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        // Yetki karari saklandi ama uygulanmasi token'daki claim'lere bagli;
        // Keycloak tarafi guncellenmezse kullanici kendi actigi alt sirketi
        // yine goremez.
        await keycloakService.AssignUserToCompanyAsync(userId, company.Id, MembershipLevels.Admin);

        return ServiceResult<CreateCompanyResponse>.SuccessAsCreated(
            new CreateCompanyResponse(company.Id),
            $"/api/v1/companies/{company.Id}");
    }
}

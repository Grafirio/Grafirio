using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Create;

public class CreateCompanyCommandHandler(
    AppDbContext context,
    IKeycloakUserService keycloakService,
    IIdentityService identityService)
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
            // kaydindan okunuyor. Claim iki ayri Keycloak ozniteliginin dogru
            // yazilip token'a yansimasina bagli ve eksik oldugunda kullanici
            // kendi firmasinin altina alt sirket bile acamiyordu; kaynak,
            // yetkinin asil yazildigi yer olmali.
            if (!isPlatformAdmin)
            {
                var isParentAdmin = await context.UserCompanyRoles.AnyAsync(
                    x => x.KeycloakUserId == userId
                         && x.CompanyId == request.ParentCompanyId.Value
                         && x.IsActive
                         && x.Role == CompanyRoles.COMPANY_ADMIN,
                    cancellationToken);

                if (!isParentAdmin)
                {
                    return ServiceResult<CreateCompanyResponse>.Error("Access denied to parent company",
                        "Alt şirket açmak için üst şirkette yönetici olmanız gerekiyor.",
                        HttpStatusCode.Forbidden);
                }
            }

            level = parentCompany.Level + 1;
        }
        else
        {
            if (!isPlatformAdmin && !identityService.HasBusinessRole(CompanyRoles.COMPANY_ADMIN))
            {
                return ServiceResult<CreateCompanyResponse>.Error("Only company admins can create root companies",
                    HttpStatusCode.Forbidden);
            }
        }

        var now = DateTime.UtcNow;

        var company = new Company
        {
            Id = NewId.NextSequentialGuid(),
            Name = request.Name,
            Code = request.Code,
            Description = request.Description,
            ParentCompanyId = request.ParentCompanyId,
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
        var role = new UserCompanyRole
        {
            Id = NewId.NextSequentialGuid(),
            KeycloakUserId = userId,
            CompanyId = company.Id,
            Role = CompanyRoles.COMPANY_ADMIN,
            IsActive = true,
            AssignedAt = now,
            AssignedBy = identityService.UserName
        };

        // Iki varlik tek SaveChanges ile yazilamiyor: MongoDB EF saglayicisi
        // cok varlikli kaydetmeyi transaction'a sariyor, calisan MongoDB ise
        // tek dugum ve transaction desteklemiyor. Geri alma bu yuzden elle.
        try
        {
            await context.UserCompanyRoles.AddAsync(role, cancellationToken);
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
        await keycloakService.AssignUserToCompanyAsync(userId, company.Id, CompanyRoles.COMPANY_ADMIN);

        return ServiceResult<CreateCompanyResponse>.SuccessAsCreated(
            new CreateCompanyResponse(company.Id),
            $"/api/v1/companies/{company.Id}");
    }
}

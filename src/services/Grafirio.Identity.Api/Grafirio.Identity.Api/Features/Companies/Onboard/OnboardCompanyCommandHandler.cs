using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Onboard;

public class OnboardCompanyCommandHandler(
    AppDbContext context,
    IKeycloakUserService keycloakService,
    IIdentityService identityService)
    : IRequestHandler<OnboardCompanyCommand, ServiceResult<OnboardCompanyResponse>>
{
    public async Task<ServiceResult<OnboardCompanyResponse>> Handle(
        OnboardCompanyCommand request, CancellationToken cancellationToken)
    {
        if (identityService.UserId == Guid.Empty)
        {
            return ServiceResult<OnboardCompanyResponse>.Error("Unauthenticated",
                HttpStatusCode.Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<OnboardCompanyResponse>.Error("Name is required",
                "Çalışma alanı adı gerekli.", HttpStatusCode.BadRequest);
        }

        var userId = identityService.UserId.ToString();

        // Tek kullanimlik olmasinin sarti bu: uyeligi olan biri buradan yeni
        // firma acamaz, normal CreateCompany ucunu ve onun yetki kurallarini
        // kullanmak zorunda.
        var alreadyBelongs = await context.UserCompanyRoles
            .AnyAsync(x => x.KeycloakUserId == userId && x.IsActive, cancellationToken);

        if (alreadyBelongs)
        {
            return ServiceResult<OnboardCompanyResponse>.Error(
                "User already belongs to a company",
                "Zaten bir çalışma alanınız var.", HttpStatusCode.Conflict);
        }

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            var codeTaken = await context.Companies
                .AnyAsync(x => x.Code == request.Code, cancellationToken);

            if (codeTaken)
            {
                return ServiceResult<OnboardCompanyResponse>.Error("Company code already exists",
                    $"'{request.Code}' kodu kullanımda.", HttpStatusCode.BadRequest);
            }
        }

        var now = DateTime.UtcNow;

        var company = new Company
        {
            Id = NewId.NextSequentialGuid(),
            Name = request.Name.Trim(),
            // Yasal ad verilmediyse görünen ad yazılıyor. Boş bırakmak, kilitli
            // bir alanı sonradan doldurma işini kullanıcıya devretmek olurdu.
            LegalName = Clean(request.LegalName) ?? request.Name.Trim(),
            Code = Clean(request.Code),
            CountryCode = Clean(request.CountryCode)?.ToUpperInvariant(),
            TeamSize = Clean(request.TeamSize),
            Description = Clean(request.Description),
            ParentCompanyId = null,
            Level = 0,
            IsActive = true,
            CreatedAt = now
        };

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
        // tek dugum ve transaction desteklemiyor ("Standalone servers do not
        // support transactions"). Diger handler'lar tek varlik yazdigi icin bu
        // sinira hic degmiyor.
        await context.Companies.AddAsync(company, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            await context.UserCompanyRoles.AddAsync(role, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Yoneticisi olmayan firma kimsenin erisemedigi bir kayit olur ve
            // kullanici da tekrar deneyemez: /onboard yalnizca hic uyeligi
            // olmayan kisi icin calisiyor, ama firma coktan olusmus oluyor.
            // Transaction olmadigi icin geri alma elle yapiliyor.
            context.Companies.Remove(company);
            await context.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        // Yetki karari burada saklaniyor ama uygulanmasi token'daki claim'lere
        // bagli; Keycloak tarafindaki company_id / business_roles guncellenmezse
        // kullanici kendi kurdugu firmayi hic goremez.
        await keycloakService.AssignUserToCompanyAsync(userId, company.Id, CompanyRoles.COMPANY_ADMIN);

        return ServiceResult<OnboardCompanyResponse>.SuccessAsCreated(
            new OnboardCompanyResponse(company.Id, CompanyRoles.COMPANY_ADMIN),
            $"/api/v1/companies/{company.Id}");
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

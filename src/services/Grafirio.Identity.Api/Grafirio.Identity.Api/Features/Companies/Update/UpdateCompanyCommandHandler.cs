using AutoMapper;
using Grafirio.Identity.Api.Features.Users;
using Grafirio.Identity.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Update;

public class UpdateCompanyCommandHandler(AppDbContext context, IIdentityService identityService, IMapper mapper)
    : IRequestHandler<UpdateCompanyCommand, ServiceResult<UpdateCompanyResponse>>
{
    public async Task<ServiceResult<UpdateCompanyResponse>> Handle(UpdateCompanyCommand request,
        CancellationToken cancellationToken)
    {
        var isPlatformAdmin = identityService.HasBusinessRole(PlatformRoles.PLATFORM_ADMIN);

        if (!isPlatformAdmin && !identityService.HasCompanyAccess(request.Id))
        {
            return ServiceResult<UpdateCompanyResponse>.Error("Access denied to company",
                HttpStatusCode.Forbidden);
        }

        var company = await context.Companies.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (company == null)
        {
            return ServiceResult<UpdateCompanyResponse>.Error("Company not found", HttpStatusCode.NotFound);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<UpdateCompanyResponse>.Error("Name is required",
                "Şirket adı gerekli.", HttpStatusCode.BadRequest);
        }

        // Kısa kod kilitli bir alan; yalnızca ilk kez dolduruluyorsa (ya da
        // platform ekibi değiştiriyorsa) benzersizliğini sormak anlamlı.
        var nextCode = Locked(company.Code, request.Code, isPlatformAdmin);
        if (!string.IsNullOrWhiteSpace(nextCode) && nextCode != company.Code)
        {
            var codeTaken = await context.Companies
                .AnyAsync(x => x.Code == nextCode && x.Id != request.Id, cancellationToken);

            if (codeTaken)
            {
                return ServiceResult<UpdateCompanyResponse>.Error("Company code already exists",
                    $"'{nextCode}' kodu kullanımda.", HttpStatusCode.BadRequest);
            }
        }

        // ── Kilitli kimlik alanları ───────────────────────────────────────
        //
        // İstemci bunları gövdede yine gönderiyor (ilk doldurma da buradan
        // yapılıyor), ama dolu bir alan gelen değere bakılmadan korunuyor.
        // Kilidi istemcinin salt-okunur çizimine bırakmak yeterli olmazdı:
        // isteği doğrudan göndermek kimseyi zorlamaz.
        company.LegalName = Locked(company.LegalName, request.LegalName, isPlatformAdmin);
        company.Code = nextCode;
        company.CountryCode = Locked(company.CountryCode, Upper(request.CountryCode), isPlatformAdmin);
        company.LegalForm = Locked(company.LegalForm, request.LegalForm, isPlatformAdmin);
        company.RegistrationNumber = Locked(company.RegistrationNumber, request.RegistrationNumber, isPlatformAdmin);
        company.TaxId = Locked(company.TaxId, request.TaxId, isPlatformAdmin);
        company.IncorporationDate = isPlatformAdmin
            ? request.IncorporationDate
            : company.IncorporationDate ?? request.IncorporationDate;

        // ── Serbest alanlar ───────────────────────────────────────────────
        company.Name = request.Name.Trim();
        company.TaxOffice = Clean(request.TaxOffice);
        company.SecondaryRegistrationNumber = Clean(request.SecondaryRegistrationNumber);
        company.LeiCode = Clean(request.LeiCode);
        company.DunsNumber = Clean(request.DunsNumber);
        company.IndustryScheme = Clean(request.IndustryScheme);
        company.IndustryCode = Clean(request.IndustryCode);
        company.IndustryDescription = Clean(request.IndustryDescription);
        company.Description = Clean(request.Description);

        company.BaseCurrency = Upper(request.BaseCurrency);
        company.Locale = Clean(request.Locale);
        company.TimeZoneId = Clean(request.TimeZoneId);
        company.FiscalYearStartMonth = request.FiscalYearStartMonth is >= 1 and <= 12
            ? request.FiscalYearStartMonth
            : null;
        company.TeamSize = Clean(request.TeamSize);

        company.GeneralPhone = Clean(request.GeneralPhone);
        company.GeneralEmail = Clean(request.GeneralEmail);
        company.Website = Clean(request.Website);
        company.EInvoiceScheme = Clean(request.EInvoiceScheme);
        company.EInvoiceAddress = Clean(request.EInvoiceAddress);

        company.AuthorizedSignatoryName = Clean(request.AuthorizedSignatoryName);
        company.AuthorizedSignatoryTitle = Clean(request.AuthorizedSignatoryTitle);
        company.DataProtectionOfficerName = Clean(request.DataProtectionOfficerName);
        company.DataProtectionOfficerEmail = Clean(request.DataProtectionOfficerEmail);
        company.PrivacyRepresentativeName = Clean(request.PrivacyRepresentativeName);
        company.PrivacyRepresentativeEmail = Clean(request.PrivacyRepresentativeEmail);
        company.PrivacyRepresentativeCountryCode = Upper(request.PrivacyRepresentativeCountryCode);

        company.Addresses = MapAddresses(request);
        company.BankAccounts = MapBankAccounts(request);

        company.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<UpdateCompanyResponse>.SuccessAsOk(new UpdateCompanyResponse(company.Id));
    }

    private List<CompanyAddress>? MapAddresses(UpdateCompanyCommand request)
    {
        // Tamamı boş bırakılmış satırlar kaydedilmiyor; kullanıcı "ekle"ye
        // basıp vazgeçtiğinde geriye boş bir adres kartı kalmasın.
        var addresses = (request.Addresses ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Line1)
                        || !string.IsNullOrWhiteSpace(x.City)
                        || !string.IsNullOrWhiteSpace(x.PostalCode))
            .Select(mapper.Map<CompanyAddress>)
            .ToList();

        foreach (var address in addresses)
        {
            address.CountryCode = Upper(address.CountryCode);
            if (string.IsNullOrWhiteSpace(address.Type) || !CompanyAddressTypes.All.Contains(address.Type))
            {
                address.Type = CompanyAddressTypes.Registered;
            }
        }

        // Bos liste null'a cevrilmiyor: EF null'i de yaziyor ve okuyabiliyor ama
        // "hic adres yok" ile "bilinmiyor" arasinda tutulacak bir fark yok, bos
        // dizi ikisini de dogru anlatiyor.
        return addresses;
    }

    private List<CompanyBankAccount>? MapBankAccounts(UpdateCompanyCommand request)
    {
        var accounts = (request.BankAccounts ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.BankName)
                        || !string.IsNullOrWhiteSpace(x.Iban)
                        || !string.IsNullOrWhiteSpace(x.AccountNumber))
            .Select(mapper.Map<CompanyBankAccount>)
            .ToList();

        foreach (var account in accounts)
        {
            // IBAN her yerde boşluklu yazılıyor ama saklarken boşluksuz ve
            // büyük harf olmalı; aksi halde aynı hesap iki farklı metin olur.
            account.Iban = Upper(account.Iban?.Replace(" ", string.Empty));
            account.SwiftBic = Upper(account.SwiftBic?.Replace(" ", string.Empty));
            account.CountryCode = Upper(account.CountryCode);
            account.Currency = Upper(account.Currency);
        }

        return accounts;
    }

    /// Dolu bir kimlik alanı, yetki yoksa gelen değere bakılmadan korunur.
    private static string? Locked(string? current, string? incoming, bool canOverride)
    {
        if (canOverride) return Clean(incoming);
        return !string.IsNullOrWhiteSpace(current) ? current : Clean(incoming);
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Upper(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

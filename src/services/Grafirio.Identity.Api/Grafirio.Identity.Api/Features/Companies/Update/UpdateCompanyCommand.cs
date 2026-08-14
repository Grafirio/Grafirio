using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.Update;

/// <summary>
/// Şirket bilgilerini günceller.
///
/// Kimlik alanları (yasal ad, kısa kod, ülke, tüzel yapı, sicil no, vergi no,
/// kuruluş tarihi) burada da taşınıyor ama bir kez dolduktan sonra yok
/// sayılıyorlar — bkz. <see cref="UpdateCompanyCommandHandler"/>. Komuttan
/// tamamen çıkarılmadılar çünkü ilk doldurma da bu uçtan yapılıyor.
/// </summary>
public record UpdateCompanyCommand(
    Guid Id,
    string Name,
    string? LegalName,
    string? Code,
    string? CountryCode,
    string? LegalForm,
    string? RegistrationNumber,
    string? TaxId,
    DateTime? IncorporationDate,
    string? TaxOffice,
    string? SecondaryRegistrationNumber,
    string? LeiCode,
    string? DunsNumber,
    string? IndustryScheme,
    string? IndustryCode,
    string? IndustryDescription,
    string? Description,
    string? BaseCurrency,
    string? Locale,
    string? TimeZoneId,
    int? FiscalYearStartMonth,
    string? TeamSize,
    string? GeneralPhone,
    string? GeneralEmail,
    string? Website,
    string? EInvoiceScheme,
    string? EInvoiceAddress,
    string? AuthorizedSignatoryName,
    string? AuthorizedSignatoryTitle,
    string? DataProtectionOfficerName,
    string? DataProtectionOfficerEmail,
    string? PrivacyRepresentativeName,
    string? PrivacyRepresentativeEmail,
    string? PrivacyRepresentativeCountryCode,
    List<CompanyAddressDto>? Addresses,
    List<CompanyBankAccountDto>? BankAccounts
) : IRequestByServiceResult<UpdateCompanyResponse>;

public record UpdateCompanyResponse(Guid Id);

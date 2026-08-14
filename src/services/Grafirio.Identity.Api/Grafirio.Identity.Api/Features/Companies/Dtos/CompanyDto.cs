namespace Grafirio.Identity.Api.Features.Companies.Dtos;

public class CompanyDto
{
    public Guid Id { get; set; }

    // Kimlik — kilitli alanlar
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Code { get; set; }
    public string? CountryCode { get; set; }
    public string? LegalForm { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? TaxId { get; set; }
    public DateTime? IncorporationDate { get; set; }

    // Kimlik — serbest
    public string? TaxOffice { get; set; }
    public string? SecondaryRegistrationNumber { get; set; }
    public string? LeiCode { get; set; }
    public string? DunsNumber { get; set; }
    public string? IndustryScheme { get; set; }
    public string? IndustryCode { get; set; }
    public string? IndustryDescription { get; set; }
    public string? Description { get; set; }

    // Operasyonel varsayılanlar
    public string? BaseCurrency { get; set; }
    public string? Locale { get; set; }
    public string? TimeZoneId { get; set; }
    public int? FiscalYearStartMonth { get; set; }
    public string? TeamSize { get; set; }

    // İletişim
    public string? GeneralPhone { get; set; }
    public string? GeneralEmail { get; set; }
    public string? Website { get; set; }
    public string? EInvoiceScheme { get; set; }
    public string? EInvoiceAddress { get; set; }

    // Temsilciler ve uyum
    public string? AuthorizedSignatoryName { get; set; }
    public string? AuthorizedSignatoryTitle { get; set; }
    public string? DataProtectionOfficerName { get; set; }
    public string? DataProtectionOfficerEmail { get; set; }
    public string? PrivacyRepresentativeName { get; set; }
    public string? PrivacyRepresentativeEmail { get; set; }
    public string? PrivacyRepresentativeCountryCode { get; set; }

    public List<CompanyAddressDto>? Addresses { get; set; }
    public List<CompanyBankAccountDto>? BankAccounts { get; set; }

    // Hiyerarşi ve durum
    public Guid? ParentCompanyId { get; set; }
    public int Level { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // "sa" gibi tam yetkili bir SQL hesabının kullanımına dair firma onayı.
    // null: henüz hiç sorulmamış.
    public bool? SaAccessConsentGiven { get; set; }
    public DateTime? SaAccessConsentGivenAt { get; set; }
    public string? SaAccessConsentGivenBy { get; set; }
    public string? SaAccessConsentTextVersion { get; set; }
}

public class CompanyAddressDto
{
    public string Type { get; set; } = string.Empty;
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public bool IsPrimary { get; set; }
}

public class CompanyBankAccountDto
{
    public string? BankName { get; set; }
    public string? BranchName { get; set; }
    public string? AccountHolder { get; set; }
    public string? CountryCode { get; set; }
    public string? Currency { get; set; }
    public string? Iban { get; set; }
    public string? AccountNumber { get; set; }
    public string? RoutingCode { get; set; }
    public string? SwiftBic { get; set; }
    public bool IsPrimary { get; set; }
}

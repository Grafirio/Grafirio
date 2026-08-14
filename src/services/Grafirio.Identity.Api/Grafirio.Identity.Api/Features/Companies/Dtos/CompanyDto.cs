namespace Grafirio.Identity.Api.Features.Companies.Dtos;

public class CompanyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
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

    public string? TaxNumber { get; set; }
    public string? TaxOffice { get; set; }
    public string? TradeRegistryNumber { get; set; }
    public string? MersisNumber { get; set; }
    public string? CompanyType { get; set; }
    public DateTime? EstablishmentDate { get; set; }
    public string? ActivityCode { get; set; }
    public string? ActivityDescription { get; set; }

    public string? LegalAddress { get; set; }
    public string? KepAddress { get; set; }
    public string? AuthorizedSignatoryName { get; set; }
    public string? AuthorizedSignatoryTitle { get; set; }
    public string? KvkkRepresentativeName { get; set; }
    public string? KvkkRepresentativeEmail { get; set; }
    public string? GeneralPhone { get; set; }
    public string? GeneralEmail { get; set; }

    public List<CompanyBankAccountDto>? BankAccounts { get; set; }
}

public class CompanyBankAccountDto
{
    public string? BankName { get; set; }
    public string? BranchName { get; set; }
    public string? Iban { get; set; }
    public string? AccountHolder { get; set; }
}
namespace Grafirio.Identity.Api.Features.Companies.Update;

public record UpdateCompanyCommand(
    Guid Id,
    string Name,
    string? Code,
    string? Description,
    string? TaxNumber,
    string? TaxOffice,
    string? TradeRegistryNumber,
    string? MersisNumber,
    string? CompanyType,
    DateTime? EstablishmentDate,
    string? ActivityCode,
    string? ActivityDescription,
    string? LegalAddress,
    string? KepAddress,
    string? AuthorizedSignatoryName,
    string? AuthorizedSignatoryTitle,
    string? KvkkRepresentativeName,
    string? KvkkRepresentativeEmail,
    string? GeneralPhone,
    string? GeneralEmail,
    List<CompanyBankAccount>? BankAccounts
) : IRequestByServiceResult<UpdateCompanyResponse>;

public record UpdateCompanyResponse(Guid Id);

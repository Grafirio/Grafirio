using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies;

public class CompanyMapping : Profile
{
    public CompanyMapping()
    {
        CreateMap<Company, CompanyDto>();
        CreateMap<CompanyAddress, CompanyAddressDto>();
        CreateMap<CompanyBankAccount, CompanyBankAccountDto>();

        // İstek gövdesinden gelen alt kayıtlar. Ters yön de gerekiyor çünkü
        // güncelleme komutu DTO taşıyor; varlığa elle kopyalamak alan
        // eklendikçe unutulmaya açık olurdu.
        CreateMap<CompanyAddressDto, CompanyAddress>();
        CreateMap<CompanyBankAccountDto, CompanyBankAccount>();
    }
}

using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies;

public class CompanyMapping : Profile
{
    public CompanyMapping()
    {
        CreateMap<Company, CompanyDto>();
    }
}
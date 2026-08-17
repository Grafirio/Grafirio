using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.GetAll;

public class GetAllCompaniesQueryHandler(ICompanyAccessService access, IMapper mapper)
    : IRequestHandler<GetAllCompaniesQuery, ServiceResult<List<CompanyDto>>>
{
    public async Task<ServiceResult<List<CompanyDto>>> Handle(GetAllCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        // Kapsam artik token'daki accessible_companies claim'inden degil uyelik
        // kayitlarindan cikiyor ve hiyerarsik: bir subede uye olmak o subenin
        // altindakileri de kapsiyor, kardes subeleri kapsamiyor. Platform ekibi
        // icin servis zaten tamamini donuyor.
        var companies = await access.AccessibleCompaniesAsync(cancellationToken);

        return ServiceResult<List<CompanyDto>>.SuccessAsOk(mapper.Map<List<CompanyDto>>(companies));
    }
}
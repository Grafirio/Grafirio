using AutoMapper;
using Grafirio.Identity.Api.Features.Departments.Dtos;

namespace Grafirio.Identity.Api.Features.Departments;

public class DepartmentMapping : Profile
{
    public DepartmentMapping()
    {
        // MemberCount varlikta yok, handler ayrica dolduruyor.
        CreateMap<Department, DepartmentDto>()
            .ForMember(d => d.MemberCount, o => o.Ignore());

        CreateMap<UserDepartment, DepartmentMemberDto>();
    }
}

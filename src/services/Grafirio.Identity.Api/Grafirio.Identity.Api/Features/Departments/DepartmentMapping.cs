using AutoMapper;
using Grafirio.Identity.Api.Features.Departments.Dtos;
using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Departments;

public class DepartmentMapping : Profile
{
    public DepartmentMapping()
    {
        // MemberCount varlikta yok, handler ayrica dolduruyor.
        //
        // Izinler ham alandan degil EffectivePermissionKeys()'ten okunuyor:
        // eski kayitlarda Permissions bos ve izin bilgisi Modules'te duruyor.
        // Modules de izinlerden turetiliyor, boylece iki alan panelde asla
        // birbirinden ayrisik gorunmuyor.
        CreateMap<Department, DepartmentDto>()
            .ForMember(d => d.MemberCount, o => o.Ignore())
            .ForMember(d => d.Permissions, o => o.MapFrom(s => s.EffectivePermissionKeys().ToList()))
            .ForMember(d => d.Modules,
                o => o.MapFrom(s => AppPermissions.ModulesOf(s.EffectivePermissionKeys())));

        CreateMap<UserDepartment, DepartmentMemberDto>();
    }
}

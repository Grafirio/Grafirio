using AutoMapper;
using Grafirio.Identity.Api.Features.Roles.Dtos;
using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Roles;

public class RoleMapping : Profile
{
    public RoleMapping()
    {
        // MemberCount varlikta yok, handler ayrica dolduruyor.
        //
        // Modules izinlerden turetiliyor, ayri bir alan olarak tutulmuyor: iki
        // liste ayri hesaplanirsa menu ile uygulanan kural sessizce ayrisir.
        CreateMap<Role, RoleDto>()
            .ForMember(d => d.MemberCount, o => o.Ignore())
            .ForMember(d => d.Modules, o => o.MapFrom(s => AppPermissions.ModulesOf(s.Permissions)));

    }
}

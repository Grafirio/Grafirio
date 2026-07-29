using AutoMapper;
using Grafirio.Identity.Api.Features.Users.Dtos;

namespace Grafirio.Identity.Api.Features.Users;

public class UserMapping : Profile
{
    public UserMapping()
    {
        CreateMap<UserCompanyRole, UserCompanyRoleDto>();
    }
}

using Grafirio.Identity.Api.Features.Users.Dtos;

namespace Grafirio.Identity.Api.Features.Users.GetByCompany;

/// <param name="IncludeRevoked">
/// Varsayılan olarak yalnızca yürürlükteki yetkiler döner; geçmiş atamalar
/// denetim amacıyla istendiğinde açılır.
/// </param>
public record GetCompanyUsersQuery(Guid CompanyId, bool IncludeRevoked = false)
    : IRequestByServiceResult<List<UserCompanyRoleDto>>;

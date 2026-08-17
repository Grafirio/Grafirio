namespace Grafirio.Identity.Api.Features.Companies.Accessible;

/// <summary>
/// Kullanıcının girebildiği şirketler — panelin şirket değiştiricisini bu
/// besliyor. Hiyerarşik: bir şubede üye olmak o şubenin altındakileri de
/// kapsıyor, ama kardeş şubeleri ya da üst şirketi kapsamıyor.
/// </summary>
public record GetAccessibleCompaniesQuery : IRequestByServiceResult<List<AccessibleCompanyDto>>;

/// <param name="Role">Kullanıcının o şirketteki etkin rolü — miras dahil.</param>
public record AccessibleCompanyDto(
    Guid Id,
    string Name,
    string? Code,
    string? CountryCode,
    Guid? ParentCompanyId,
    int Level,
    string? Role);

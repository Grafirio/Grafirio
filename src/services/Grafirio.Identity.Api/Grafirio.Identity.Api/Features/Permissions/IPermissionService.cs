namespace Grafirio.Identity.Api.Features.Permissions;

/// <summary>
/// "Bu kullanıcı şu şirkette şu modüle girebilir mi" sorusunun tek cevabı.
///
/// Handler'lar artık rol değil izin soruyor: kural değişince yirmi dosya değil
/// <see cref="AppModules"/> güncelleniyor.
/// </summary>
public interface IPermissionService
{
    Task<EffectivePermissions> ForCompanyAsync(Guid companyId, CancellationToken ct);

    Task<bool> CanAsync(Guid companyId, string module, CancellationToken ct);
}

/// <param name="Role">Kullanıcının şirketteki etkin rolü; erişimi yoksa null.</param>
/// <param name="Modules">Girebileceği modüller.</param>
/// <param name="RestrictedByDepartment">
/// Modül kümesinin departman ataması yüzünden daraltılıp daraltılmadığı.
/// Panelde "neden bu menüyü göremiyorum" sorusunu cevaplamak için: kısıt
/// rolden mi departmandan mı geliyor.
/// </param>
public record EffectivePermissions(
    Guid CompanyId,
    string? Role,
    IReadOnlyList<string> Modules,
    bool RestrictedByDepartment);

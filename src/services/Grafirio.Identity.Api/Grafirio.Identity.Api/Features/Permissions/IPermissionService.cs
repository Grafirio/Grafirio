namespace Grafirio.Identity.Api.Features.Permissions;

/// <summary>
/// "Bu kullanıcı şu şirkette şunu yapabilir mi" sorusunun tek cevabı.
///
/// Handler'lar rol değil izin soruyor: kural değişince yirmi dosya değil
/// <see cref="AppPermissions"/> güncelleniyor.
/// </summary>
public interface IPermissionService
{
    Task<EffectivePermissions> ForCompanyAsync(Guid companyId, CancellationToken ct);

    /// <summary>
    /// Belirli bir iznin (<see cref="AppPermissions"/>) verilip verilmediği.
    /// Şirkete erişim kontrolünü de kapsıyor: erişimi olmayanın rolü null,
    /// rolü null olanın izni yok.
    /// </summary>
    Task<bool> CanAsync(Guid companyId, string permission, CancellationToken ct);

    /// <summary>
    /// Modülün herhangi bir iznine sahip mi — "bu menüyü görebilir mi".
    /// Ekranı açmak için, ekranda bir şeyi değiştirmek için değil.
    /// </summary>
    Task<bool> CanSeeModuleAsync(Guid companyId, string module, CancellationToken ct);
}

/// <param name="Role">Kullanıcının şirketteki etkin rolü; erişimi yoksa null.</param>
/// <param name="Modules">Girebileceği modüller — izinlerden türetiliyor.</param>
/// <param name="Permissions">Etkin izinler (MODÜL.AKSİYON).</param>
/// <param name="RestrictedByDepartment">
/// İzin kümesinin departman ataması yüzünden daraltılıp daraltılmadığı.
/// Panelde "neden bu menüyü göremiyorum" sorusunu cevaplamak için: kısıt
/// rolden mi departmandan mı geliyor.
/// </param>
public record EffectivePermissions(
    Guid CompanyId,
    string? Role,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Permissions,
    bool RestrictedByDepartment);

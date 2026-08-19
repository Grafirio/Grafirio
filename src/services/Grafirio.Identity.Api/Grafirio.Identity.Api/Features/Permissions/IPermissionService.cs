using Grafirio.Shared.Identity.Permissions;
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

// EffectivePermissions burada tanimli degil: cevabin bicimi paylasilan
// pakette (Grafirio.Shared.Identity.Permissions), cunku DataAnalysis.Api de
// ayni govdeyi okuyor. Iki tarafin ayri tanimladigi bir sozlesme, sessizce
// ayrisan bir sozlesmedir.

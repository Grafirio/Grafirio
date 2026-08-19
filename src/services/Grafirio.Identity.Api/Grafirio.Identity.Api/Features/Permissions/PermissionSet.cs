using Grafirio.Shared.Identity.Permissions;

namespace Grafirio.Identity.Api.Features.Permissions;

/// <summary>
/// İzin listelerinin temizlenmesi.
///
/// Tanınmayan anahtarlar süzülüyor: istemciden gelen serbest metnin izin
/// kümesine sızması, ileride o metin gerçek bir anahtara dönüştüğünde sessiz
/// bir yetki açılışı olurdu.
///
/// Önceki <c>PermissionPolicy</c>'nin kalanı bu kadar. Rol tavanı
/// (<c>CeilingForRole</c>) ve modül geri dönüşü silindi: tavan diye bir şey
/// kalmadı, izinler artık birleşiyor.
/// </summary>
public static class PermissionSet
{
    /// <summary>
    /// Yazılacak izin listesini temizler. <see cref="AppPermissions.PanelRead"/>
    /// süzülüyor — panele giriş üyelikle geliyor, izin kümesinin konusu değil.
    /// </summary>
    public static List<string> Sanitize(IEnumerable<string>? permissions) =>
        [.. (permissions ?? [])
            .Where(AppPermissions.IsValid)
            .Where(p => p != AppPermissions.PanelRead)
            .Distinct()];
}

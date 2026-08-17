namespace Grafirio.Identity.Api.Features.Permissions;

/// <summary>
/// Çağıranın belirtilen şirketteki etkin izinleri. Panel menüyü buna göre
/// çiziyor — ama asıl kontrol sunucuda: gizlenmiş bir menü, isteğin doğrudan
/// gönderilmesini engellemez.
/// </summary>
public record GetMyPermissionsQuery(Guid CompanyId) : IRequestByServiceResult<EffectivePermissions>;

using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.GetCurrent;

/// <summary>
/// Çağıranın kendi şirketi.
///
/// Panel bu bilgiyi önce token'daki <c>company_id</c> ile bulup
/// <c>GET /companies</c> listesinden eşleştiriyordu. O liste de
/// <c>accessible_companies</c> claim'iyle süzüldüğü için, iki ayrı Keycloak
/// özniteliğinin de doğru yazılmış ve token'a yansımış olması gerekiyordu;
/// biri eksik olduğunda kullanıcı kayıt sırasında kendi kurduğu şirketi bile
/// göremiyor, sayfa boş açılıyordu. Burada kaynak Mongo'daki üyelik kaydı,
/// yani yetkinin asıl yazıldığı yer.
/// </summary>
public record GetCurrentCompanyQuery : IRequestByServiceResult<CurrentCompanyResponse>;

/// <param name="Company">Şirketin kendisi.</param>
/// <param name="Role">Çağıranın bu şirketteki rolü.</param>
/// <param name="CanEditIdentity">
/// Dolu kimlik alanlarını değiştirme yetkisi. Şirket yöneticisi boş bir alanı
/// bir kez doldurabilir ama dolmuş olanı değiştiremez; bu yetki platform
/// ekibinde. İstemci alanı buna göre kilitli çiziyor, kuralın kendisi yine
/// sunucuda (bkz. <see cref="Update.UpdateCompanyCommandHandler"/>).
/// </param>
public record CurrentCompanyResponse(CompanyDto Company, string? Role, bool CanEditIdentity);

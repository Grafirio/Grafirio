using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.GetCurrent;

/// <summary>
/// Panelin üzerinde çalıştığı şirket.
///
/// <paramref name="CompanyId"/> verilirse o şirket açılır — şirket
/// değiştiricinin seçimi buradan geçiyor. Verilmezse kullanıcının erişebildiği
/// ilk şirkete düşülüyor.
///
/// Kaynak, token'daki <c>company_id</c> claim'i değil veritabanındaki üyelik
/// kayıtları: claim iki ayrı Keycloak özniteliğinin doğru yazılıp token'a
/// yansımasına bağlıydı ve eksik olduğunda kullanıcı kayıt sırasında kendi
/// kurduğu şirketi bile göremiyordu.
/// </summary>
public record GetCurrentCompanyQuery(Guid? CompanyId = null)
    : IRequestByServiceResult<CurrentCompanyResponse>;

/// <param name="Company">Şirketin kendisi.</param>
/// <param name="Role">Çağıranın bu şirketteki etkin rolü — üst şirketten miras dahil.</param>
/// <param name="CanEditIdentity">
/// Dolu kimlik alanlarını değiştirme yetkisi. Şirket yöneticisi boş bir alanı
/// bir kez doldurabilir ama dolmuş olanı değiştiremez; bu yetki platform
/// ekibinde. Kuralın kendisi sunucuda (bkz. <see cref="Update.UpdateCompanyCommandHandler"/>).
/// </param>
public record CurrentCompanyResponse(CompanyDto Company, string? Role, bool CanEditIdentity);

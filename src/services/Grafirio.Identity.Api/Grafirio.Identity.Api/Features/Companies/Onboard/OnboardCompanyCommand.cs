namespace Grafirio.Identity.Api.Features.Companies.Onboard;

/// <summary>
/// Yeni kaydolan kişinin kendi ilk çalışma alanını kurması.
///
/// <see cref="Create.CreateCompanyCommand"/> kök firma açmak için çağıranın
/// zaten COMPANY_ADMIN olmasını şart koşuyor; bu kurumsal senaryoda doğru,
/// rastgele biri var olan bir yapının yanına şirket açamasın diye. Ama self
/// servis kayıtta kimsenin henüz rolü yok: kişi şirketi kuramıyor, kuramadığı
/// için yönetici olamıyor, yönetici olamadığı için abonelik başlatamıyor.
/// Zincir daha ilk adımda kopuyordu.
///
/// Bu uç o kilidi yalnızca bir kez açıyor: çağıranın hiçbir aktif firma üyeliği
/// yoksa kök firma kurulur ve kişi o firmanın yöneticisi yapılır. Üyeliği olan
/// biri için çalışmaz, dolayısıyla var olan firmaların yanına yeni firma açmanın
/// yolu değil.
/// </summary>
/// <param name="CountryCode">
/// ISO 3166-1 alpha-2. Burada soruluyor çünkü hangi vergi ve sicil alanlarının
/// isteneceğini bu belirliyor ve alan bir kez dolduktan sonra kilitleniyor;
/// kayıt sırasında alınmazsa kullanıcı sonradan kilitli bir alanı doldurmak
/// zorunda kalırdı.
/// </param>
/// <param name="TeamSize">
/// Önceden <see cref="Description"/> içine "Ekip büyüklüğü: 6–20" diye
/// yazılıyordu; şirket açıklaması alanında kullanıcının karşısına o çıkıyordu.
/// </param>
public record OnboardCompanyCommand(
    string Name,
    string? LegalName,
    string? Code,
    string? CountryCode,
    string? TeamSize,
    string? Description
) : IRequestByServiceResult<OnboardCompanyResponse>;

public record OnboardCompanyResponse(Guid CompanyId, string Role);

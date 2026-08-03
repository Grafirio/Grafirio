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
public record OnboardCompanyCommand(
    string Name,
    string? Code,
    string? Description
) : IRequestByServiceResult<OnboardCompanyResponse>;

public record OnboardCompanyResponse(Guid CompanyId, string Role);

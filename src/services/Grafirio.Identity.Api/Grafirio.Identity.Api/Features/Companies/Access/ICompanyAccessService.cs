namespace Grafirio.Identity.Api.Features.Companies.Access;

/// <summary>
/// "Bu kullanıcı şu şirkete erişebilir mi" sorusunun tek cevabı.
///
/// Daha önce bu soru token'daki <c>accessible_companies</c> claim'inden
/// cevaplanıyordu. Claim, veritabanındaki üyeliklerin önbelleğiydi ama
/// geçersiz kılma mekanizması yoktu: Keycloak'a yazma sessizce başarısız
/// olabiliyor, token yenilenene kadar da eski kalıyordu. Şirket Ayarları'nın
/// açılmaması, yeni açılan alt şirketin görünmemesi ve alt şirket
/// eklenememesi — hepsi buradan çıktı. Identity verinin sahibi olduğu için
/// burada önbelleğe hiç gerek yok, kaynak doğrudan okunuyor.
///
/// Erişim hiyerarşik: bir şirketteki rol o şirket ve tüm altları için geçerli.
/// Kontrol, hedef şirketin <see cref="Company.Path"/> zinciriyle kullanıcının
/// üyelik kayıtlarının kesişimine bakıyor.
/// </summary>
public interface ICompanyAccessService
{
    /// Kullanıcı bu şirkete (ya da bir üstüne) üye mi.
    Task<bool> CanAccessAsync(Guid companyId, CancellationToken ct);

    /// Kullanıcının bu şirkette belirtilen rolü var mı — üst şirketten miras dahil.
    Task<bool> HasRoleAsync(Guid companyId, string role, CancellationToken ct);

    /// Erişilebilen tüm şirketler; hiyerarşide altta kalanlar dahil.
    Task<List<Company>> AccessibleCompaniesAsync(CancellationToken ct);

    /// Kullanıcının bu şirketteki etkin rolü, yoksa null.
    Task<string?> EffectiveRoleAsync(Guid companyId, CancellationToken ct);
}

namespace Grafirio.Bridge;

/// <summary>
/// Kurulumun ve baglantinin kullaniciya nasil gosterilecegi.
///
/// Neden ayri bir arayuz: ayni cekirdek iki farkli kabukta calisiyor. Sunucu
/// odasina kurulan surum bir Windows servisi — ekrani yok, gordugu tek sey
/// gunluk. Masaustune kurulan surum ise bir pencere: kurulum kodunu buyuk
/// puntoyla gostermesi, tarayiciyi kendisi acmasi bekleniyor.
///
/// Onceki surumde kod dogrudan <c>Console.WriteLine</c> ile yaziliyordu. Bu,
/// cekirdegi tek bir kabuga baglamak demekti: pencereli surumde konsol diye
/// bir sey yok ve kurulum kodu hicbir yere dusmuyordu.
///
/// <b>Gunluk bunun disinda.</b> Ne olup bittigi her durumda gunluge yaziliyor;
/// burasi yalnizca "kullaniciya nasil gosterilecek" sorusunun cevabi.
/// </summary>
public interface IBridgeDisplay
{
    /// <summary>Kuran kisiye gosterilecek onay kodu.</summary>
    void ShowDeviceCode(DeviceCodePrompt prompt);

    /// <summary>Bridge'in o anki durumu.</summary>
    void ShowStatus(BridgeStatus status, string? detail = null);
}

/// <summary>
/// Kuran kisiye gosterilecekler.
///
/// <paramref name="VerificationUriComplete"/> kodu adresin icinde tasiyor;
/// tarayici bununla acilirsa kimsenin elle kod girmesi gerekmiyor. Kimlik
/// sunucusu bunu vermek zorunda degil, o yuzden bos kalabiliyor.
/// </summary>
public record DeviceCodePrompt(
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete)
{
    /// <summary>Tarayicida acilacak adres — varsa kodu icinde taşıyan.</summary>
    public string BestUri => string.IsNullOrWhiteSpace(VerificationUriComplete)
        ? VerificationUri
        : VerificationUriComplete;
}

public enum BridgeStatus
{
    /// <summary>Kayit yok; kurulum onayi istenecek.</summary>
    AwaitingEnrollment,

    /// <summary>Kod ekranda, kuran kisinin onayi bekleniyor.</summary>
    AwaitingApproval,

    /// <summary>
    /// Onay alindi, buluta kaydolunuyor.
    ///
    /// Bu ara durum olmadan kullanici onayladiktan sonra ekranda hicbir sey
    /// degismiyordu: kart hâlâ "onay bekleniyor" diyordu ve onayin gecip
    /// gecmedigi anlasilmiyordu.
    /// </summary>
    Registering,

    /// <summary>
    /// Onay alindi ama kayit yapilamadi — cogu zaman buluta ulasilamiyor.
    /// Sebep <c>detail</c> ile geliyor; sebepsiz bir basarisizlik kullaniciyi
    /// kendi kodunu suclarken birakir.
    /// </summary>
    EnrollmentFailed,

    /// <summary>Kayit tamam, buluta baglaniliyor.</summary>
    Connecting,

    /// <summary>Bulut baglantisi ayakta.</summary>
    Connected,

    /// <summary>Baglanti koptu; yeniden deneniyor.</summary>
    Disconnected,

    /// <summary>Servis durdu.</summary>
    Stopped,
}

/// <summary>
/// Konsol kabugu: servis olarak ya da dogrudan konsoldan calistirilan surum.
///
/// Kutu yalnizca konsoldan calistiranlar icin; servis olarak kuruldugunda
/// stdout hicbir yere akmiyor ve o durumda gorunur olan sey
/// <see cref="BridgeDeviceLogin"/>'in yazdigi gunluk satiri.
/// </summary>
public class ConsoleBridgeDisplay : IBridgeDisplay
{
    public void ShowDeviceCode(DeviceCodePrompt prompt)
    {
        // Kisa alanlar kutunun icinde, adresler disinda: bir URL cerceveden
        // uzun oldugunda hizalama bozuluyor ve kutu kirik gorunuyor. Kutunun
        // isi kodu one cikarmak; adres zaten kopyalanacak bir metin.
        Console.WriteLine(
            $"""

             ┌─────────────────────────────────────────────────────────────┐
             │  Grafirio Bridge kurulumu                                   │
             ├─────────────────────────────────────────────────────────────┤
             │  Tarayıcıda aşağıdaki adresi açıp şu kodu girin:            │
             │                                                             │
             │      {prompt.UserCode,-55}│
             └─────────────────────────────────────────────────────────────┘

               {prompt.VerificationUri}

             Kodu elle girmek istemezseniz doğrudan bu adres:

               {prompt.BestUri}

             Onay bekleniyor…
             """);
    }

    /// <summary>
    /// Durum konsola yazilmiyor: zaten gunluge yaziliyor ve konsoldan
    /// calistirildiginda gunluk de ekrana dusuyor. Ikisini birden yazmak ayni
    /// satiri iki kez gostermek olurdu.
    /// </summary>
    public void ShowStatus(BridgeStatus status, string? detail = null)
    {
    }
}

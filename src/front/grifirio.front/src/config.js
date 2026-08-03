// Vitrin hicbir API cagirmiyor; tek bilmesi gereken, dugmelerinin nereye
// gittigi. Adresler ortama gore degistigi icin derleme aninda veriliyor.
export const APP_URL = import.meta.env.VITE_APP_URL || 'https://admin.grafirio.com';

const KEYCLOAK_URL = import.meta.env.VITE_KEYCLOAK_URL || 'https://login.grafirio.com';
const KEYCLOAK_REALM = import.meta.env.VITE_KEYCLOAK_REALM || 'grafirio';
const KEYCLOAK_CLIENT_ID = import.meta.env.VITE_KEYCLOAK_CLIENT_ID || 'grafirio-client';

// "Giris yap" uygulamaya gidiyor; uygulama zaten login-required ile aciliyor ve
// Keycloak'in giris ekranina yolluyor.
export const SIGN_IN_URL = APP_URL;

/**
 * Kayit, Keycloak'in registrations ucuna DOGRUDAN gidiyor.
 *
 * Uygulamaya yollamak ise yaramiyor: useradmin login-required ile aciliyor ve
 * daha ilk render olmadan Keycloak'a yonleniyor, yani araya girip "bu kisi
 * kayit olmak istiyor" diyecek bir yer yok. Sonuc olarak "Ucretsiz basla"
 * dugmesi kullaniciyi giris ekranina birakiyordu.
 *
 * Kayit bitince Keycloak oturumu aciyor ve buraya donuyor; uygulama kendi
 * baslangicini yapip kullaniciyi dogrudan karsilama sihirbazina aliyor.
 */
export const signUpUrl = (planCode) => {
  const redirectUri = planCode
    ? `${APP_URL}/onboarding?plan=${encodeURIComponent(planCode)}`
    : `${APP_URL}/onboarding`;

  const params = new URLSearchParams({
    client_id: KEYCLOAK_CLIENT_ID,
    response_type: 'code',
    scope: 'openid',
    redirect_uri: redirectUri,
  });

  return `${KEYCLOAK_URL}/realms/${KEYCLOAK_REALM}/protocol/openid-connect/registrations?${params}`;
};

export const SIGN_UP_URL = signUpUrl();

// Paket dugmeleri secimi tasiyor ki kullanici ayni secimi karsilama
// sihirbazinda bir daha yapmasin. Firma adimi bilerek odemeden once geliyor:
// abonelik firmaya baglaniyor ve firmasi olmayan bir alicinin odemesi
// abonelik acmadan dusuyor.
export const planCheckoutUrl = (planCode) => signUpUrl(planCode);

export const CONTACT_EMAIL = 'merhaba@grafirio.com';

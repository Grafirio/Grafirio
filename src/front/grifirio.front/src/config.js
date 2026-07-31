// Vitrin hicbir API cagirmiyor; tek bilmesi gereken, "giris yap" ve "basla"
// dugmelerinin nereye gittigi. Alan adi ortama gore degistigi icin derleme
// aninda veriliyor, koda gomulmuyor.
export const APP_URL = import.meta.env.VITE_APP_URL || 'https://admin.grafirio.com';

export const SIGN_IN_URL = APP_URL;
export const SIGN_UP_URL = APP_URL;

// Paket dugmeleri secimi tasiyor ki kullanici ayni secimi karsilama
// sihirbazinda bir daha yapmasin. Firma adimi bilerek odemeden once geliyor:
// abonelik firmaya baglaniyor ve firmasi olmayan bir alicinin odemesi
// aboneli acmadan dusuyor.
export const planCheckoutUrl = (planCode) =>
  `${APP_URL}/onboarding?plan=${encodeURIComponent(planCode)}`;

export const CONTACT_EMAIL = 'merhaba@grafirio.com';
    
import axios from 'axios';

const GATEWAY = import.meta.env.VITE_API_URL || '';

const auth = (token) => ({ headers: { Authorization: `Bearer ${token}` }, timeout: 20000 });

/**
 * Satilabilir paketler. Kodlar Identity'deki SubscriptionPlans.Sellable ile
 * birebir ayni olmak zorunda; ayrisirlarsa musteri aldigini sandigi seyi
 * almamis olur.
 */
export const PLANS = [
  {
    code: 'MONTHLY',
    name: 'Aylık',
    price: '$15',
    unit: '/ ay',
    trialDays: 15,
    description: 'İlk 15 gün ücretsiz. Deneme bitince aylık ücretlendirme başlar.',
    features: [
      'Sınırsız kanvas ve paylaşım',
      'Sohbet ederek analiz',
      'Departman ve yetki yönetimi',
      'Öncelikli destek',
    ],
  },
  {
    code: 'CREDIT',
    name: 'Kredi yüklemeli',
    price: 'Yüklediğin kadar',
    unit: '',
    trialDays: 0,
    description: 'Ön ödemeli bakiye. Aylık taahhüt yok, denemesi de yok.',
    features: [
      'Bakiye bittiğinde durur',
      'İstediğin zaman yükle',
      'Aylık taahhüt yok',
      'Aynı ürün özellikleri',
    ],
  },
];

export const planByCode = (code) => PLANS.find((p) => p.code === code) || null;

/**
 * Kaydolan kisinin ilk calisma alani.
 *
 * Normal "create company" ucu cagiranin zaten COMPANY_ADMIN olmasini istiyor;
 * yeni kaydolanin hicbir rolu olmadigi icin oradan gecemiyor ve akis daha ilk
 * adimda 403 ile duruyordu. Bu uc kisiyi kendi kurdugu firmanin yoneticisi
 * yapiyor ve yalnizca uyeligi olmayan biri icin calisiyor.
 */
export const onboardCompany = async (token, { name, code, description }) => {
  const { data } = await axios.post(
    `${GATEWAY}/v1/identity/companies/onboard`,
    { name, code: code || null, description: description || null },
    auth(token)
  );
  return data?.data ?? data;
};

/**
 * Abonelik baslatma. Odeme saglayicisi henuz baglanmadigi icin kart bilgisi
 * istenmiyor ve tahsilat yapilmiyor; abonelik dogrudan aciliyor. Saglayici
 * secilince tahsilat bu cagrinin oncesine giriyor, sonrasi degismiyor.
 */
export const startSubscription = async (token, planCode) => {
  const { data } = await axios.post(
    `${GATEWAY}/v1/identity/subscriptions/start`,
    { plan: planCode },
    auth(token)
  );
  return data?.data ?? data;
};

/** Sunucunun hata govdesinden kullaniciya gosterilebilir bir cumle cikarir. */
export const describeError = (err, fallback) =>
  err?.response?.data?.detail ||
  err?.response?.data?.title ||
  err?.response?.data?.message ||
  fallback;

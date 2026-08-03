import axios from 'axios';

const GATEWAY = import.meta.env.VITE_API_URL || '';

const auth = (token) => ({ headers: { Authorization: `Bearer ${token}` }, timeout: 20000 });

// Vitrindeki paket adlariyla ayni; kod SubscriptionPlans ile birebir.
export const PLAN_NAMES = {
  MONTHLY: 'Aylık',
  CREDIT: 'Kredi yüklemeli',
  TRIAL: 'Deneme (eski)',
  STANDARD: 'Takım (eski)',
  ENTERPRISE: 'Kurumsal (eski)',
};

/**
 * Firmanin abonelik kayitlari, en yeniden eskiye. GetCompanySubscriptions ucu
 * kendi firmana erisimin varsa calisiyor (identity.HasCompanyAccess), platform
 * yetkisi gerekmiyor.
 */
export const fetchSubscriptions = async (token, companyId) => {
  const { data } = await axios.get(
    `${GATEWAY}/v1/identity/subscriptions/company/${companyId}`,
    auth(token)
  );
  return data?.data ?? data ?? [];
};

export const describeError = (err, fallback) =>
  err?.response?.data?.detail || err?.response?.data?.title || fallback;

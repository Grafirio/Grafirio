import axios from 'axios';
import keycloak from '../keycloak';

// Tum gateway cagrilari icin ortak istemci.
export const GATEWAY = import.meta.env.VITE_GATEWAY_URL || '';

const client = axios.create({ timeout: 30000 });

// Token her istekte yeniden okunur: uzun oturumlarda yenilenmis olabilir ve
// bir kez yakalanan token sessizce suresi dolmus olarak gonderilirdi.
client.interceptors.request.use(async (config) => {
  if (keycloak.authenticated) {
    try {
      await keycloak.updateToken(30);
    } catch {
      // Yenilenemiyorsa istek 401 alacak ve cagiran taraf hatayi gosterecek;
      // burada sessizce oturum kapatmak kullaniciyi is ortasinda atar.
    }
    config.headers.Authorization = `Bearer ${keycloak.token}`;
  }
  return config;
});

export default client;

/** Sunucudan gelen ProblemDetails'i okunabilir tek satira indirger. */
export const describeError = (error) => {
  const data = error?.response?.data;
  if (!data) return error?.message || 'Bilinmeyen hata';
  if (typeof data === 'string') return data;
  const parts = [data.title, data.detail, data.error].filter(Boolean);
  return parts.length ? parts.join(' — ') : `HTTP ${error.response.status}`;
};

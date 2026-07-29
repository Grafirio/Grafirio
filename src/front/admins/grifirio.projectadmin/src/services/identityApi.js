import axios from 'axios';
import keycloak from '../keycloak';

// Tum cagrilar gateway uzerinden; identity rotasi orada /api/v1/... e cevriliyor.
const GATEWAY = import.meta.env.VITE_GATEWAY_URL || '';
const IDENTITY = `${GATEWAY}/v1/identity`;

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

/** Sunucudan gelen ProblemDetails'i okunabilir tek satira indirger. */
export const describeError = (error) => {
  const data = error?.response?.data;
  if (!data) return error?.message || 'Bilinmeyen hata';
  if (typeof data === 'string') return data;
  const parts = [data.title, data.detail].filter(Boolean);
  return parts.length ? parts.join(' — ') : `HTTP ${error.response.status}`;
};

export const getCompanies = async () => (await client.get(`${IDENTITY}/companies`)).data;

export const createCompany = async ({ name, code, description, parentCompanyId }) =>
  (await client.post(`${IDENTITY}/companies`, {
    name,
    code: code || null,
    description: description || null,
    parentCompanyId: parentCompanyId || null,
  })).data;

export const getCompanyUsers = async (companyId, includeRevoked = false) =>
  (await client.get(`${IDENTITY}/users/company/${companyId}`, {
    params: { includeRevoked },
  })).data;

export const registerUser = async (payload) =>
  (await client.post(`${IDENTITY}/users/register`, payload)).data;

export const assignRole = async ({ keycloakUserId, companyId, role }) =>
  (await client.post(`${IDENTITY}/users/roles`, { keycloakUserId, companyId, role })).data;

export const revokeRole = async (keycloakUserId, companyId) =>
  (await client.delete(`${IDENTITY}/users/${keycloakUserId}/companies/${companyId}/role`)).data;

export const getCompanySubscriptions = async (companyId) =>
  (await client.get(`${IDENTITY}/subscriptions/company/${companyId}`)).data;

export const createSubscription = async ({ companyId, plan, startsAt, endsAt, orderId }) =>
  (await client.post(`${IDENTITY}/subscriptions`, {
    companyId,
    plan,
    startsAt: startsAt || null,
    endsAt: endsAt || null,
    orderId: orderId || null,
  })).data;

export const cancelSubscription = async (id) =>
  (await client.delete(`${IDENTITY}/subscriptions/${id}`)).data;

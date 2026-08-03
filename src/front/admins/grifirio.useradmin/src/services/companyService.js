import axios from 'axios';

const GATEWAY = import.meta.env.VITE_API_URL || '';

const auth = (token) => ({ headers: { Authorization: `Bearer ${token}` }, timeout: 20000 });

const unwrap = (data) => data?.data ?? data;

export const COMPANY_ROLES = [
  {
    code: 'COMPANY_ADMIN',
    name: 'Yönetici',
    description: 'Şirket bilgilerini düzenler, kullanıcı ve yetki tanımlar.',
  },
  {
    code: 'COMPANY_MANAGER',
    name: 'Müdür',
    description: 'Veri kaynaklarını ve dashboard’ları yönetir; yetki dağıtamaz.',
  },
  {
    code: 'COMPANY_USER',
    name: 'Kullanıcı',
    description: 'Kendisine açılan dashboard’ları görür ve soru sorar.',
  },
];

export const roleName = (code) =>
  COMPANY_ROLES.find((r) => r.code === code)?.name ?? code;

/** Erisilebilen firmalar; alt sirket agaci da bu listeden kuruluyor. */
export const fetchCompanies = async (token) => {
  const { data } = await axios.get(`${GATEWAY}/v1/identity/companies`, auth(token));
  return unwrap(data) ?? [];
};

export const fetchCompanyUsers = async (token, companyId, includeRevoked = false) => {
  const { data } = await axios.get(
    `${GATEWAY}/v1/identity/users/company/${companyId}?includeRevoked=${includeRevoked}`,
    auth(token)
  );
  return unwrap(data) ?? [];
};

export const assignRole = async (token, { keycloakUserId, companyId, role }) => {
  const { data } = await axios.post(
    `${GATEWAY}/v1/identity/users/roles`,
    { keycloakUserId, companyId, role },
    auth(token)
  );
  return unwrap(data);
};

export const revokeRole = async (token, { keycloakUserId, companyId }) => {
  await axios.delete(
    `${GATEWAY}/v1/identity/users/${encodeURIComponent(keycloakUserId)}/companies/${companyId}/role`,
    auth(token)
  );
};

/**
 * Alt sirket. Buradaki uc genis yetkili olan: cagiran ust firmaya erisebiliyor
 * ve yonetici olmak zorunda. Kayit akisindaki /onboard ucuyla karistirilmamali,
 * o yalnizca hic uyeligi olmayan kisinin ilk calisma alani icin.
 */
export const createSubCompany = async (token, { name, code, description, parentCompanyId }) => {
  const { data } = await axios.post(
    `${GATEWAY}/v1/identity/companies`,
    {
      name,
      code: code || null,
      description: description || null,
      parentCompanyId,
    },
    auth(token)
  );
  return unwrap(data);
};

export const describeError = (err, fallback) =>
  err?.response?.data?.detail ||
  err?.response?.data?.title ||
  err?.response?.data?.message ||
  fallback;

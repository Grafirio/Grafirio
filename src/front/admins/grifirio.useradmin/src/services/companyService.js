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

/** Sirket kimlik/yasal/adres/banka alanlarini kaydeder (PUT). */
export const updateCompany = async (token, companyId, payload) => {
  const { data } = await axios.put(
    `${GATEWAY}/v1/identity/companies/${companyId}`,
    payload,
    auth(token)
  );
  return unwrap(data);
};

export const COMPANY_DOCUMENT_TYPES = [
  { code: 'VERGI_LEVHASI', name: 'Vergi levhası' },
  { code: 'IMZA_SIRKULERI', name: 'İmza sirküleri' },
  { code: 'TICARET_SICIL_GAZETESI', name: 'Ticaret sicil gazetesi' },
  { code: 'FAALIYET_BELGESI', name: 'Faaliyet belgesi' },
  { code: 'DIGER', name: 'Diğer' },
];

export const documentTypeName = (code) =>
  COMPANY_DOCUMENT_TYPES.find((t) => t.code === code)?.name ?? code;

export const fetchCompanyDocuments = async (token, companyId) => {
  const { data } = await axios.get(
    `${GATEWAY}/v1/identity/companies/${companyId}/documents`,
    auth(token)
  );
  return unwrap(data) ?? [];
};

export const uploadCompanyDocument = async (token, companyId, { file, documentType, expiryDate, note }) => {
  const form = new FormData();
  form.append('file', file);
  form.append('documentType', documentType);
  if (expiryDate) form.append('expiryDate', expiryDate);
  if (note) form.append('note', note);

  const { data } = await axios.post(
    `${GATEWAY}/v1/identity/companies/${companyId}/documents`,
    form,
    { headers: { Authorization: `Bearer ${token}` }, timeout: 30000 }
  );
  return unwrap(data);
};

export const deleteCompanyDocument = async (token, companyId, documentId) => {
  await axios.delete(
    `${GATEWAY}/v1/identity/companies/${companyId}/documents/${documentId}`,
    auth(token)
  );
};

/** Belgeyi indirir ve tarayicida kaydetme diyalogunu tetikler. */
export const downloadCompanyDocument = async (token, companyId, documentId, fileName) => {
  const response = await axios.get(
    `${GATEWAY}/v1/identity/companies/${companyId}/documents/${documentId}/download`,
    { headers: { Authorization: `Bearer ${token}` }, timeout: 30000, responseType: 'blob' }
  );
  const url = window.URL.createObjectURL(new Blob([response.data]));
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName || 'belge';
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
};

export const describeError = (err, fallback) =>
  err?.response?.data?.detail ||
  err?.response?.data?.title ||
  err?.response?.data?.message ||
  fallback;

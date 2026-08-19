import axios from 'axios';

const GATEWAY = import.meta.env.VITE_API_URL || '';

const auth = (token) => ({ headers: { Authorization: `Bearer ${token}` }, timeout: 20000 });

const unwrap = (data) => data?.data ?? data;


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

/**
 * Uyelik seviyesi: admin ya da uye. Kurucu buradan verilemiyor — sirketi
 * kuran e-postaya bagli ve devri ayri bir akis.
 */
export const setMembershipLevel = async (token, { keycloakUserId, companyId, level }) => {
  const { data } = await axios.post(
    `${GATEWAY}/v1/identity/users/membership`,
    { keycloakUserId, companyId, level },
    auth(token)
  );
  return unwrap(data);
};

/** Uyeligi kapatir; kisinin o sirketteki rol atamalari da kapaniyor. */
export const revokeMembership = async (token, { keycloakUserId, companyId }) => {
  await axios.delete(
    `${GATEWAY}/v1/identity/users/${encodeURIComponent(keycloakUserId)}/companies/${companyId}/membership`,
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

/**
 * Cagiranin kendi sirketi.
 *
 * Sayfa bunu once token'daki company_id ile bulup /companies listesinden
 * esletiyordu; o liste de accessible_companies claim'iyle suzuldugu icin iki
 * ayri Keycloak ozniteliginin de dogru yazilmis olmasi gerekiyordu ve biri
 * eksik oldugunda kullanici kayit sirasinda kendi kurdugu sirketi bile
 * goremiyordu. Bu uc kaynagi Mongo'daki uyelik kaydi.
 *
 * { company, role, canEditIdentity } doner.
 */
export const fetchCurrentCompany = async (token, companyId) => {
  const url = companyId
    ? `${GATEWAY}/v1/identity/companies/current?companyId=${companyId}`
    : `${GATEWAY}/v1/identity/companies/current`;
  const { data } = await axios.get(url, auth(token));
  return unwrap(data);
};

/**
 * Kullanicinin girebildigi sirketler — sirket degistiriciyi bu besliyor.
 *
 * Hiyerarsik: bir subede uye olmak o subenin altindakileri de kapsiyor ama
 * kardes subeleri ya da ust sirketi kapsamiyor. Kaynak token claim'i degil
 * sunucudaki uyelik kayitlari.
 */
export const fetchAccessibleCompanies = async (token) => {
  const { data } = await axios.get(`${GATEWAY}/v1/identity/companies/accessible`, auth(token));
  return unwrap(data) ?? [];
};

/**
 * Cagiranin bu sirketteki etkin izinleri: { companyId, role, modules,
 * permissions, restrictedByDepartment }.
 *
 * modules menuyu, permissions ise ekran icindeki butonlari belirliyor. Asil
 * kontrol sunucuda: gizlenmis bir buton, istegin dogrudan gonderilmesini
 * engellemez.
 */
export const fetchMyPermissions = async (token, companyId) => {
  const { data } = await axios.get(
    `${GATEWAY}/v1/identity/permissions/me/${companyId}`,
    auth(token)
  );
  return unwrap(data);
};

/**
 * Modul basina aksiyon listesi: [{ module, permissions: ['ROLES.READ', ...] }].
 *
 * Izin matrisi bunu okuyarak ciziliyor; sabit listeyi istemciye
 * kopyalamak iki tarafin sessizce ayrisma yolu olurdu.
 */
export const fetchPermissionActions = async (token) => {
  const { data } = await axios.get(`${GATEWAY}/v1/identity/permissions/actions`, auth(token));
  return unwrap(data) ?? [];
};


/**
 * Bir sirketin dogrudan alt sirketleri.
 *
 * Onceden /companies listesi parentCompanyId'ye gore suzuluyordu, ama o liste
 * accessible_companies claim'iyle sinirli: yeni acilan alt sirket claim'e
 * yansiyana kadar gorunmuyor, claim hic yoksa liste bastan bos kaliyordu.
 * Bu uc hiyerarsiyi dogrudan okuyor.
 */
export const fetchCompanyChildren = async (token, companyId) => {
  const { data } = await axios.get(
    `${GATEWAY}/v1/identity/companies/${companyId}/children`,
    auth(token)
  );
  return unwrap(data) ?? [];
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

/**
 * Belge turleri ulkeden bagimsiz adlandirildi; asagidaki adlar Turkiye'deki
 * karsiliklari. "Vergi levhasi" yalnizca burada o adla var ama karsiligi olan
 * mukellefiyet belgesi her yerde var.
 */
export const COMPANY_DOCUMENT_TYPES = [
  { code: 'TAX_CERTIFICATE', name: 'Vergi levhası / mükellefiyet belgesi' },
  { code: 'INCORPORATION_CERTIFICATE', name: 'Kuruluş belgesi' },
  { code: 'REGISTRY_EXTRACT', name: 'Sicil kaydı / faaliyet belgesi' },
  { code: 'ARTICLES_OF_ASSOCIATION', name: 'Ana sözleşme' },
  { code: 'SIGNATURE_AUTHORIZATION', name: 'İmza sirküleri' },
  { code: 'VAT_CERTIFICATE', name: 'KDV kayıt belgesi' },
  { code: 'BANK_LETTER', name: 'Banka hesap teyidi' },
  { code: 'INSURANCE', name: 'Sigorta poliçesi' },
  { code: 'LICENSE', name: 'Lisans / ruhsat' },
  { code: 'OTHER', name: 'Diğer' },
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

/**
 * Belgenin kucuk onizlemesi.
 *
 * Dogrudan <img src> kullanilamiyor: uc kimlik dogrulama istiyor ve tarayici
 * img isteklerine Authorization basligi eklemiyor. Blob olarak cekilip nesne
 * URL'ine cevriliyor — cagiran taraf isi bitince revokeObjectURL ile birakmali,
 * yoksa her listeleme bellekte birikir.
 */
export const fetchCompanyDocumentThumbnail = async (token, companyId, documentId) => {
  const response = await axios.get(
    `${GATEWAY}/v1/identity/companies/${companyId}/documents/${documentId}/thumbnail`,
    { headers: { Authorization: `Bearer ${token}` }, timeout: 20000, responseType: 'blob' }
  );
  return window.URL.createObjectURL(response.data);
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

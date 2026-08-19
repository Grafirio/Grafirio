import axios from 'axios';

const GATEWAY = import.meta.env.VITE_API_URL || '';

const auth = (token) => ({ headers: { Authorization: `Bearer ${token}` }, timeout: 20000 });

const unwrap = (data) => data?.data ?? data;

/**
 * Roller sirkete bagli: alt sirketler ayri tuzel kisilik ve kendi izin
 * semalarini tasiyorlar. Ayni kisi bir sirkette muhasebeci, digerinde
 * finansci olabiliyor — bu yuzden her cagri sirket kimligi tasiyor.
 */
export const fetchRoles = async (token, companyId) => {
  const { data } = await axios.get(
    `${GATEWAY}/v1/identity/roles/company/${companyId}`,
    auth(token)
  );
  return unwrap(data) ?? [];
};

export const createRole = async (token, payload) => {
  const { data } = await axios.post(`${GATEWAY}/v1/identity/roles`, payload, auth(token));
  return unwrap(data);
};

export const updateRole = async (token, roleId, payload) => {
  const { data } = await axios.put(
    `${GATEWAY}/v1/identity/roles/${roleId}`,
    payload,
    auth(token)
  );
  return unwrap(data);
};

export const deleteRole = async (token, roleId) => {
  await axios.delete(`${GATEWAY}/v1/identity/roles/${roleId}`, auth(token));
};


export const assignUserToRole = async (token, roleId, keycloakUserId) => {
  await axios.post(
    `${GATEWAY}/v1/identity/roles/${roleId}/members`,
    { keycloakUserId },
    auth(token)
  );
};

export const removeUserFromRole = async (token, roleId, keycloakUserId) => {
  await axios.delete(
    `${GATEWAY}/v1/identity/roles/${roleId}/members/${encodeURIComponent(keycloakUserId)}`,
    auth(token)
  );
};

/**
 * Bir kisinin bir sirketteki yetkisinin tamami: uyelik seviyesi, rolleri,
 * kisisel izinleri ve birlesimi.
 *
 * Tek parca geliyor cunku panelde sorulan sey "bu izin nereden geliyor":
 * uc ayri istekten birlestirilen bir tablo, cok rollu birinde tutarsiz
 * gorunebilir.
 */
export const fetchUserAccess = async (token, companyId, keycloakUserId) => {
  const { data } = await axios.get(
    `${GATEWAY}/v1/identity/permissions/users/${companyId}/${encodeURIComponent(keycloakUserId)}`,
    auth(token)
  );
  return unwrap(data);
};

/** Kisisel izinler. Liste tam gonderiliyor: eksik gelen anahtar kaldirilmis sayiliyor. */
export const setUserPermissions = async (token, companyId, keycloakUserId, permissions) => {
  await axios.put(
    `${GATEWAY}/v1/identity/permissions/users/${companyId}/${encodeURIComponent(keycloakUserId)}`,
    { permissions },
    auth(token)
  );
};

import axios from 'axios';

const GATEWAY = import.meta.env.VITE_API_URL || '';

const auth = (token) => ({ headers: { Authorization: `Bearer ${token}` }, timeout: 20000 });

const unwrap = (data) => data?.data ?? data;

/**
 * Departmanlar sirkete (subeye) bagli: alt sirketler ayri tuzel kisilik ve
 * kendi verilerini yonetiyorlar, dolayisiyla "Ankara / Muhasebe" ile
 * "Istanbul / Muhasebe" ayri kayitlar.
 */
export const fetchDepartments = async (token, companyId) => {
  const { data } = await axios.get(
    `${GATEWAY}/v1/identity/departments/company/${companyId}`,
    auth(token)
  );
  return unwrap(data) ?? [];
};

export const createDepartment = async (token, payload) => {
  const { data } = await axios.post(`${GATEWAY}/v1/identity/departments`, payload, auth(token));
  return unwrap(data);
};

export const updateDepartment = async (token, departmentId, payload) => {
  const { data } = await axios.put(
    `${GATEWAY}/v1/identity/departments/${departmentId}`,
    payload,
    auth(token)
  );
  return unwrap(data);
};

export const deleteDepartment = async (token, departmentId) => {
  await axios.delete(`${GATEWAY}/v1/identity/departments/${departmentId}`, auth(token));
};

export const fetchDepartmentMembers = async (token, departmentId) => {
  const { data } = await axios.get(
    `${GATEWAY}/v1/identity/departments/${departmentId}/members`,
    auth(token)
  );
  return unwrap(data) ?? [];
};

export const assignUserToDepartment = async (token, departmentId, keycloakUserId) => {
  await axios.post(
    `${GATEWAY}/v1/identity/departments/${departmentId}/members`,
    { keycloakUserId },
    auth(token)
  );
};

export const removeUserFromDepartment = async (token, departmentId, keycloakUserId) => {
  await axios.delete(
    `${GATEWAY}/v1/identity/departments/${departmentId}/members/${encodeURIComponent(keycloakUserId)}`,
    auth(token)
  );
};

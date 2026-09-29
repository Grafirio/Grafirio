import client, { GATEWAY, describeError } from './authClient';

export { describeError };

// Tum cagrilar gateway uzerinden; identity rotasi orada /api/v1/... e cevriliyor.
const IDENTITY = `${GATEWAY}/v1/identity`;

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

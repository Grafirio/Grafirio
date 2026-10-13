import axios from 'axios';
import client, { describeError, GATEWAY } from './authClient';
import { AUDIT_LOG_LIMIT, OPS_REQUEST_TIMEOUT_MS, RECOVERY_URL } from '../constants/ops';

// The gateway is itself a monitored service, so a direct agent URL keeps the panel usable
// when the gateway is down. Without it, calls go through the gateway's /ops route.
const AGENT_URL = import.meta.env.VITE_OPS_AGENT_URL;
const OPS_BASE = AGENT_URL ? `${AGENT_URL}/api/ops` : `${GATEWAY}/ops`;

const request = async ({ url, ...config }) => {
  const { data } = await client.request({ timeout: OPS_REQUEST_TIMEOUT_MS, ...config, url: OPS_BASE + url });
  return data;
};

export const getOpsStatus = (signal) => request({ url: '/status', signal });

export const controlService = (serviceId, action) =>
  request({ method: 'post', url: `/services/${encodeURIComponent(serviceId)}/${encodeURIComponent(action)}` });

export const startDocker = () => request({ method: 'post', url: '/docker/start' });

export const bringInfrastructureUp = () => request({ method: 'post', url: '/infrastructure/up' });

export const updateAutoHeal = (patch) => request({ method: 'put', url: '/settings/autoheal', data: patch });

export const getAuditLog = () => request({ url: '/audit', params: { limit: AUDIT_LOG_LIMIT } });

export const isCancelled = (error) => axios.isCancel(error);

export const describeOpsError = (error) => {
  if (!error.response) {
    return `Ops ajanına ulaşılamadı. Ajan çalışıyor mu? Docker veya Keycloak kapalıysa kurtarma ekranını kullanın: ${RECOVERY_URL}`;
  }
  return error.response.data?.error ?? describeError(error);
};

const GATEWAY_URL = import.meta.env?.VITE_API_URL || 'http://localhost:5000';

export const OPS_SUMMARY_URL = `${GATEWAY_URL}/ops/status/summary`;
export const OUTAGE_POLL_INTERVAL_MS = 30000;
export const OUTAGE_REQUEST_TIMEOUT_MS = 4000;
export const OUTAGE_STATUS_DEGRADED = 'degraded';

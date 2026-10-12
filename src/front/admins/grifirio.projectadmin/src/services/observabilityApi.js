import client, { describeError, GATEWAY } from './authClient';

export const OBSERVABILITY_SERVICES = [
  { id: 'gateway', label: 'Gateway' },
  { id: 'identity', label: 'Identity API' },
  { id: 'commerce', label: 'Commerce API' },
  { id: 'data-analysis', label: 'Data Analysis API' },
];

export async function getObservability(params, signal) {
  return Promise.all(OBSERVABILITY_SERVICES.map(async (service) => {
    try {
      const { data } = await client.get(GATEWAY + '/admin/observability/' + service.id,
        { params, signal, timeout: 8000 });
      if (!data?.runtime || !Array.isArray(data.metrics) || !Array.isArray(data.logs) || !Array.isArray(data.traces)) {
        throw new Error('Telemetri API yanıtı geçersiz; gateway adresini ve servis sürümünü kontrol edin.');
      }
      return { ...service, data };
    } catch (error) {
      return { ...service, error: describeError(error), status: error.response?.status };
    }
  }));
}

import client, { GATEWAY } from './authClient';
const base = import.meta.env.VITE_TELEMETRY_ARCHIVE_URL || `${GATEWAY}/admin/telemetry`;
export async function getArchiveEvents(params, signal) {
  const { data } = await client.get(`${base}/events`, { params, signal });
  if (!Array.isArray(data?.items) || !Number.isSafeInteger(data.total) || !Number.isSafeInteger(data.snapshot)) throw new Error('Arşiv liste yanıtı geçersiz. Servis adresini kontrol edin.');
  return data;
}
export async function getArchiveStatus(signal) {
  const { data } = await client.get(`${base}/status`, { signal });
  if (!data?.settings || !data.loss || !Number.isSafeInteger(data.count) || !Array.isArray(data.producers)) throw new Error('Arşiv durum yanıtı geçersiz.');
  return data;
}
export async function saveArchiveSettings(settings) {
  const { data } = await client.put(`${base}/settings`, settings);
  if (!Number.isSafeInteger(data?.retentionDays) || !Number.isSafeInteger(data.maxRows) || !Number.isSafeInteger(data.maxBytes)) throw new Error('Arşiv ayarlarının kaydedildiği doğrulanamadı.');
  return data;
}

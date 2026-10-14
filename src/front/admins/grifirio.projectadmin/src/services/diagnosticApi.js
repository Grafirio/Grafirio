import client, { GATEWAY } from './authClient';

const BASE = `${GATEWAY}/data-analysis/api/admin/diagnostics`;
export const getDiagnostics = async (params) => (await client.get(BASE, { params })).data;
export const getDiagnostic = async (id) => (await client.get(`${BASE}/${id}`)).data;
export const getDiagnosticSummary = async (days) => (await client.get(`${BASE}/summary`, { params: { days } })).data;
export const saveReview = (id, data) => client.put(`${BASE}/${id}/review`, data);
export const getDiagnosticSettings = async () => (await client.get(`${BASE}/settings`)).data;
export const saveDiagnosticSettings = async (data) => (await client.put(`${BASE}/settings`, data)).data;
export const getDiagnosticEvents = async () => (await client.get(`${BASE}/admin-events`)).data;
export const getDiagnosticTests = async () => (await client.get(`${BASE}/tests`)).data;
export const createDiagnosticTest = (data) => client.post(`${BASE}/tests`, data);
export const updateDiagnosticTest = (id, data) => client.put(`${BASE}/tests/${id}`, data);
export const deleteDiagnosticTest = (id) => client.delete(`${BASE}/tests/${id}`);
export const runDiagnosticTest = async (id) => (await client.post(`${BASE}/tests/${id}/run`, {}, { timeout: 660000 })).data;

export const STATUS = { preparing: 'Hazırlanıyor', submitted: 'İletildi', processing: 'Çalışıyor', completed: 'Tamamlandı',
  failed: 'Başarısız', clarification: 'Açıklama gerekiyor', cancelled: 'İptal edildi', cancelling: 'İptal ediliyor' };
export const ERRORS = { empty_result: 'Kayıt bulunamadı', insufficient_data: 'Veri yetersiz', timeout: 'Süre aşıldı',
  entity_filter_mismatch: 'Ad / kod eşleme çelişkisi',
  invalid_parameters: 'Geçersiz parametreler', scope_mismatch: 'Kapsam uyuşmazlığı', translation_failed: 'Soru çevrilemedi',
  request_rejected: 'İstek reddedildi', execution_failed: 'Çalıştırma hatası' };
export const STAGES = { received: 'İstek alındı', translation: 'Soru yorumlandı', validation: 'Parametre doğrulama', execution: 'Analiz çalıştırma' };
export const REVIEWS = { new: 'Yeni', investigating: 'İnceleniyor', fixed: 'Düzeltildi', verified: 'Doğrulandı' };

export function flatten(value, path = '', result = {}) {
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    for (const [key, item] of Object.entries(value)) flatten(item, path ? `${path}.${key}` : key, result);
  } else result[path] = JSON.stringify(value) ?? '—';
  return result;
}

export function comparisonRows(left, right) {
  const a = flatten(left); const b = flatten(right);
  return [...new Set([...Object.keys(a), ...Object.keys(b)])].sort().map((key) => ({ key, left: a[key] ?? '—', right: b[key] ?? '—', changed: a[key] !== b[key] }));
}

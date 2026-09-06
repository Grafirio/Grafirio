import { getConnectionSummary, testConnection, testConnectionDraft } from '../dataAnalysisService.js';

const SAVED_FIELDS = ['host', 'port', 'database', 'username', 'trustServerCertificate'];

export default async function testDesktopConnectionForm(connectionInfo, connectionId) {
  if (connectionInfo.password !== '') return testConnectionDraft(connectionInfo);
  if (!connectionId) {
    throw new Error('Kaydetmeden test etmek için veritabanı parolasını girin.');
  }

  const summary = await getConnectionSummary(connectionId);
  if (SAVED_FIELDS.some((field) => String(connectionInfo[field]) !== String(summary[field]))) {
    throw new Error('Değişiklikleri kaydetmeden test etmek için parolayı girin veya Kaydet düğmesine basın.');
  }
  return testConnection(connectionId);
}
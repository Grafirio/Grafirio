import DesktopConnectionError from './DesktopConnectionError.js';

export const CONNECTION_CONTEXT_TIMEOUT_MS = 25000;
const CONNECTION_CONTEXT = 'connectionContext';
const SESSION_EXPIRED = 'sessionExpired';
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

const MAX_REASON_LENGTH = 200;

// Masaüstü kabuğunun bildirdiği sebep. Uzunluğu sınırlı: bu metin hata kutusuna basılıyor ve
// sınırsız bir dize, okunmak istenen cümleyi ekrandan taşırırdı.
const readReason = (data) =>
  typeof data.reason === 'string' && data.reason.trim()
    ? data.reason.trim().slice(0, MAX_REASON_LENGTH)
    : null;

export default function requestDesktopConnectionContext() {
  const host = globalThis.window;
  const webview = host?.chrome?.webview;
  if (!host?.__GRAFIRIO_DESKTOP__ || host.top !== host.self
    || !webview?.addEventListener || !webview?.removeEventListener || !webview?.postMessage) {
    return Promise.reject(new DesktopConnectionError(
      'unavailable', 'Bu sayfa masaüstü uygulamasında açılmamış'));
  }

  return new Promise((resolve, reject) => {
    const requestId = globalThis.crypto.randomUUID();
    let timeout;
    const finish = (error, context) => {
      clearTimeout(timeout);
      webview.removeEventListener('message', handleMessage);
      if (error) reject(error);
      else resolve(context);
    };
    const handleMessage = ({ data }) => {
      if (!data || typeof data !== 'object' || Array.isArray(data)) return;
      if (data.type === SESSION_EXPIRED) {
        finish(new DesktopConnectionError('signedOut'));
        return;
      }
      if (data.type !== CONNECTION_CONTEXT || data.requestId !== requestId) return;
      if (data.error || typeof data.bridgeId !== 'string'
        || !GUID_PATTERN.test(data.bridgeId) || data.bridgeId === EMPTY_GUID) {
        finish(new DesktopConnectionError(
          data.error === 'signedOut' ? 'signedOut' : 'unavailable', readReason(data)));
        return;
      }
      finish(null, { bridgeId: data.bridgeId });
    };

    // Never use window messages or cache an identity across native sessions.
    try {
      webview.addEventListener('message', handleMessage);
      timeout = setTimeout(() => finish(new DesktopConnectionError(
        'unavailable', 'Masaüstü uygulaması yanıt vermedi')), CONNECTION_CONTEXT_TIMEOUT_MS);
      webview.postMessage({ type: CONNECTION_CONTEXT, requestId });
    } catch {
      finish(new DesktopConnectionError('unavailable', 'Masaüstü uygulamasına ulaşılamadı'));
    }
  });
}
const REQUEST_TIMEOUT_MS = 150_000;
const pending = new Map();
let queue = Promise.resolve();
let registerFlush;
const flushHandler = new Promise(resolve => { registerFlush = resolve; });

export function onFlush(handler) { registerFlush(handler); }

async function acknowledgeFlush(id) {
  try {
    await (await flushHandler)();
    window.chrome.webview.postMessage({ event: 'workspace-flushed', id, ok: true });
  } catch {
    // The application displays the actual error; do not send user input back in lifecycle errors.
    window.chrome.webview.postMessage({ event: 'workspace-flushed', id, ok: false, error: 'Pending work could not be saved.' });
  }
}

window.chrome.webview.addEventListener('message', ({ data }) => {
  if (data?.event === 'workspace-flush' && typeof data.id === 'string') {
    void acknowledgeFlush(data.id);
    return;
  }
  const request = pending.get(data?.id);
  if (!request) return;
  pending.delete(data.id);
  clearTimeout(request.timer);
  if (data.ok) request.resolve(data.result);
  else request.reject(new Error(data.error || 'İşlem tamamlanamadı.'));
});

function send(method, payload) {
  return new Promise((resolve, reject) => {
    const id = crypto.randomUUID();
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error('Yanıt zaman aşımı. Devam eden işlemi iptal edin.'));
    }, REQUEST_TIMEOUT_MS);
    pending.set(id, { resolve, reject, timer });
    try { window.chrome.webview.postMessage({ id, method, payload }); }
    catch (error) {
      pending.delete(id);
      clearTimeout(timer);
      reject(error);
    }
  });
}

export function request(method, payload = {}) {
  if (method === 'cancel') return send(method, payload);
  const operation = queue.then(() => send(method, payload));
  // Keep the queue usable while returning the original rejection to its caller.
  queue = operation.then(() => undefined, () => undefined);
  return operation;
}
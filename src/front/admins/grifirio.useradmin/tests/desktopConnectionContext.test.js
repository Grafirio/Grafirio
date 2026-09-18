import test from 'node:test';
import assert from 'node:assert/strict';
import requestContext, { CONNECTION_CONTEXT_TIMEOUT_MS } from '../src/services/connections/requestDesktopConnectionContext.js';

const BRIDGE_ID = 'aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb';

function fixture(context) {
  const listeners = new Set();
  const sent = [];
  const host = {
    __GRAFIRIO_DESKTOP__: true,
    chrome: { webview: {
      addEventListener(type, listener) { assert.equal(type, 'message'); listeners.add(listener); },
      removeEventListener(type, listener) { listeners.delete(listener); },
      postMessage(message) { sent.push(message); },
    } },
  };
  host.top = host.self = host;
  const original = globalThis.window;
  globalThis.window = host;
  context.after(() => { globalThis.window = original; });
  const emit = (data) => [...listeners].forEach((listener) => listener({ data }));
  const reply = (data = {}, request = sent.at(-1)) => emit({
    type: 'connectionContext', requestId: request.requestId, bridgeId: BRIDGE_ID, ...data,
  });
  return { host, listeners, sent, emit, reply };
}

test('context uses only native channel, UUID correlation and exact message contract', async (context) => {
  const { sent, reply, emit, listeners } = fixture(context);
  const pending = requestContext();
  assert.equal(sent[0].type, 'connectionContext');
  assert.match(sent[0].requestId, /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i);
  assert.deepEqual(Object.keys(sent[0]).sort(), ['requestId', 'type']);
  emit(JSON.stringify({ type: 'connectionContext', requestId: sent[0].requestId, bridgeId: BRIDGE_ID }));
  reply({ requestId: 'wrong' });
  reply({ type: 'tokens' });
  assert.equal(listeners.size, 1);
  reply();
  assert.deepEqual(await pending, { bridgeId: BRIDGE_ID });
  assert.equal(listeners.size, 0);
});

test('context never caches the bridge and correlates concurrent requests', async (context) => {
  const { sent, reply, listeners } = fixture(context);
  const first = requestContext();
  const second = requestContext();
  assert.notEqual(sent[0].requestId, sent[1].requestId);
  reply({}, sent[1]);
  await second;
  assert.equal(listeners.size, 1);
  reply({}, sent[0]);
  await first;
  const next = requestContext();
  assert.equal(sent.length, 3);
  reply();
  await next;
});

for (const response of [
  { error: 'signedOut' }, { error: 'unavailable' }, { error: 'notReady' },
  { bridgeId: undefined }, { bridgeId: 'invalid' }, { bridgeId: '00000000-0000-0000-0000-000000000000' },
]) {
  test(`invalid/unready context fails closed: ${JSON.stringify(response)}`, async (context) => {
    const { reply, listeners } = fixture(context);
    const pending = requestContext();
    reply(response);
    await assert.rejects(pending, /Bu bilgisayardan bağlantı kurulamadı/);
    assert.equal(listeners.size, 0);
  });
}

// Sebep taşınmasaydı her arıza aynı "Masaüstü bağlantısı hazır değil" yazısıyla çıkardı:
// oturum düşmesi, buluta bağlanamama ve zaman aşımı ayırt edilemezdi ve gerçek sebebi
// öğrenmenin tek yolu kullanıcının makinesindeki günlük dosyası olurdu.
test('reported reason reaches the message, trimmed, capped and never invented', async (context) => {
  const { reply } = fixture(context);

  const withReason = requestContext();
  reply({ error: 'unavailable', reason: '  Oturum gerekli. Lütfen yeniden giriş yapın.  ' });
  await assert.rejects(withReason, (error) => {
    assert.match(error.message, /\(Oturum gerekli\. Lütfen yeniden giriş yapın\.\)$/);
    assert.equal(error.reason, 'Oturum gerekli. Lütfen yeniden giriş yapın.');
    return true;
  });

  // Sınırsız bir dize, okunmak istenen cümleyi ekrandan taşırırdı.
  const long = requestContext();
  reply({ error: 'unavailable', reason: 'x'.repeat(500) });
  await assert.rejects(long, (error) => {
    assert.equal(error.reason.length, 200);
    return true;
  });

  // Sebep yoksa uydurulmuyor; mesaj eskisi gibi kalıyor.
  for (const reason of [undefined, '   ', 42, { detail: 'nesne' }]) {
    const bare = requestContext();
    reply({ error: 'unavailable', reason });
    await assert.rejects(bare, (error) => {
      assert.equal(error.reason, null);
      assert.doesNotMatch(error.message, /\(/);
      return true;
    });
  }
});

test('timeout is exactly 25 seconds, cleans listener, and permits ordinary retry', async (context) => {
  const { listeners, reply } = fixture(context);
  context.mock.timers.enable({ apis: ['setTimeout'] });
  const pending = requestContext();
  const rejected = assert.rejects(pending, /Bu bilgisayardan bağlantı kurulamadı/);
  assert.equal(CONNECTION_CONTEXT_TIMEOUT_MS, 25000);
  context.mock.timers.tick(CONNECTION_CONTEXT_TIMEOUT_MS - 1);
  assert.equal(listeners.size, 1);
  context.mock.timers.tick(1);
  await rejected;
  assert.equal(listeners.size, 0);
  const retry = requestContext();
  reply();
  await retry;
});

test('missing channel, nested frame, post failure and session expiry reject without fallback', async (context) => {
  const { host, emit, listeners } = fixture(context);
  host.top = {};
  await assert.rejects(requestContext());
  host.top = host;
  const pending = requestContext();
  emit({ type: 'sessionExpired' });
  await assert.rejects(pending, { code: 'signedOut' });
  host.chrome.webview.postMessage = () => { throw new Error('unavailable'); };
  await assert.rejects(requestContext());
  assert.equal(listeners.size, 0);
  delete host.chrome;
  await assert.rejects(requestContext());
});
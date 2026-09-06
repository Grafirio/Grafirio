import test, { mock } from 'node:test';
import assert from 'node:assert/strict';
import axios from 'axios';
import { readFile } from 'node:fs/promises';

mock.module('../src/keycloak.js', { defaultExport: { token: 'test-access-token' } });
const service = await import('../src/services/dataAnalysisService.js');
const { default: testForm } = await import('../src/services/connections/testDesktopConnectionForm.js');
const BRIDGE_ID = 'aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb';
const OTHER_BRIDGE_ID = 'bbbbbbbb-1111-2222-3333-aaaaaaaaaaaa';
const INFO = {
  name: 'Source', host: 'localhost', port: 1433, database: 'Sample',
  username: 'reader', password: 'transient-secret', trustServerCertificate: true,
};

function fixture(context, desktop = true) {
  const calls = [];
  const sent = [];
  const listeners = new Set();
  const state = { native: { bridgeId: BRIDGE_ID }, summary: {
    ...INFO, password: undefined, connectionMode: 'bridge', bridgeId: BRIDGE_ID,
  } };
  const host = { __GRAFIRIO_DESKTOP__: desktop, chrome: { webview: {
    addEventListener(type, listener) { assert.equal(type, 'message'); listeners.add(listener); },
    removeEventListener(type, listener) { assert.equal(type, 'message'); listeners.delete(listener); },
    postMessage(request) {
      sent.push(request);
      [...listeners].forEach((listener) => listener({ data: { ...request, ...state.native } }));
    },
  } } };
  host.top = host.self = host;
  const originalWindow = globalThis.window;
  const originalAdapter = axios.defaults.adapter;
  globalThis.window = host;
  axios.defaults.adapter = async (config) => {
    calls.push({ url: config.url, method: config.method, data: config.data ? JSON.parse(config.data) : null });
    return { data: config.method === 'get' && config.url.endsWith('/api/connections/saved')
      ? state.summary : { success: true, connectionId: 'saved', databases: ['Sample'] },
    status: 200, statusText: 'OK', headers: {}, config };
  };
  context.after(() => { globalThis.window = originalWindow; axios.defaults.adapter = originalAdapter; });
  return { calls, sent, state, host };
}

test('desktop create/update override caller mode and bridge with fresh native context', async (context) => {
  const { calls, sent } = fixture(context);
  const foreign = { ...INFO, connectionMode: 'direct', bridgeId: OTHER_BRIDGE_ID };
  await service.saveConnection('Source', foreign);
  await service.updateConnection('saved', { ...foreign, password: '' });
  assert.equal(sent.length, 2);
  for (const call of calls) {
    assert.equal(call.data.connectionMode, 'bridge');
    assert.equal(call.data.bridgeId, BRIDGE_ID);
  }
  assert.equal(calls[0].data.password, INFO.password);
  assert.equal('password' in calls[1].data, false);
});

test('draft test and database list use native bridge, all host types allowed, no persistence', async (context) => {
  const { calls, sent } = fixture(context);
  for (const host of ['localhost', '192.168.1.10', '10.8.0.5', 'sql.public.example']) {
    await testForm({ ...INFO, host }, 'foreign-saved');
    await service.getDatabases({ ...INFO, host });
  }
  assert.equal(sent.length, 8);
  for (const call of calls) {
    assert.match(call.url, /\/api\/connections\/(test|databases)$/);
    assert.equal(call.method, 'post');
    assert.equal(call.data.bridgeId, BRIDGE_ID);
    assert.equal(call.data.connectionMode, 'bridge');
    assert.equal(call.data.password, INFO.password);
  }
});

const savedOperations = [
  () => service.testConnection('saved'),
  () => service.getTables('saved'),
  () => service.getTableColumns('saved', 'dbo.Orders'),
  () => service.getDataQuality('saved', []),
  () => service.getStatistics('saved', []),
  () => service.getMissingData('saved', []),
  () => service.getRelationships('saved', []),
  () => service.startAnalysis('saved'),
  () => service.submitAnalysisAnswers('saved', {}),
  () => service.submitAgentQuery('saved', 'Question'),
  () => service.saveSelectedTables('saved', []),
  () => service.learnFact('saved', {}),
];

test('every saved database/AI path validates stored routing against fresh native identity', async (context) => {
  const { calls, sent } = fixture(context);
  for (const operation of savedOperations) await operation();
  assert.equal(sent.length, savedOperations.length);
  assert.equal(calls.length, savedOperations.length * 2);
  for (let index = 0; index < calls.length; index += 2) {
    assert.equal(calls[index].url.endsWith('/api/connections/saved'), true);
    assert.equal(calls[index].method, 'get');
  }
  const query = calls.find((call) => call.url.endsWith('/api/agent/query'));
  assert.deepEqual(query.data, { connectionId: 'saved', question: 'Question', parentQueryId: null });
});

test('foreign, direct and absent saved metadata block execution without rewriting connection', async (context) => {
  const { state, calls } = fixture(context);
  for (const summary of [
    { connectionMode: 'bridge', bridgeId: BRIDGE_ID, routeAvailable: false },
    { connectionMode: 'bridge', bridgeId: OTHER_BRIDGE_ID },
    { connectionMode: 'direct', bridgeId: BRIDGE_ID }, {},
  ]) {
    state.summary = summary;
    for (const operation of savedOperations) await assert.rejects(operation(), /Bu bilgisayardan bağlantı kurulamadı/);
  }
  assert.equal(calls.every((call) => call.method === 'get' && call.url.endsWith('/api/connections/saved')), true);
});

test('missing/unready native bridge prevents any HTTP, including credentials and saves', async (context) => {
  const { state, calls, host } = fixture(context);
  const operations = [...savedOperations,
    () => service.saveConnection('Source', INFO),
    () => service.updateConnection('saved', INFO),
    () => service.testConnectionDraft(INFO),
    () => service.getDatabases(INFO),
  ];
  for (const error of ['signedOut', 'unavailable']) {
    state.native = { error };
    for (const operation of operations) await assert.rejects(operation(), /Bu bilgisayardan bağlantı kurulamadı/);
  }
  delete host.chrome;
  for (const operation of operations) await assert.rejects(operation());
  assert.equal(calls.length, 0);
});

test('blank-password desktop test never saves changed fields or silently rebinds a foreign record', async (context) => {
  const { calls, state } = fixture(context);
  await assert.rejects(testForm({ ...INFO, host: 'changed', password: '' }, 'saved'), /parolayı girin/);
  state.summary.bridgeId = OTHER_BRIDGE_ID;
  await assert.rejects(testForm({ ...INFO, password: '' }, 'saved'), /bu bilgisayara bağlı değil/);
  assert.equal(calls.every((call) => call.method === 'get'), true);
});

test('blank-password unchanged desktop form probes saved connection without updating it', async (context) => {
  const { calls } = fixture(context);
  await testForm({ ...INFO, password: '' }, 'saved');
  assert.equal(calls.at(-1).url.endsWith('/api/connections/saved/test'), true);
  assert.equal(calls.at(-1).data, null);
  assert.equal(calls.filter((call) => call.method !== 'get').length, 1);
});

test('browser payloads and saved request paths remain unchanged without native requests', async (context) => {
  const { calls, sent } = fixture(context, false);
  await service.saveConnection('Source', INFO);
  await service.updateConnection('saved', { ...INFO, password: '' });
  for (const operation of savedOperations) await operation();
  assert.equal(sent.length, 0);
  assert.equal(calls.length, savedOperations.length + 2);
  assert.deepEqual(calls[0].data, INFO);
  assert.equal('connectionMode' in calls[1].data, false);
  assert.equal('bridgeId' in calls[1].data, false);
  assert.equal('password' in calls[1].data, false);
});

test('unavailable draft API fails clearly without save or direct fallback', async (context) => {
  const { sent } = fixture(context);
  let attempts = 0;
  axios.defaults.adapter = async () => {
    attempts++;
    throw new Error('404 Not Found');
  };
  await assert.rejects(service.testConnectionDraft(INFO), /Bu bilgisayardan bağlantı kurulamadı/);
  await assert.rejects(service.getDatabases(INFO), /Bu bilgisayardan bağlantı kurulamadı/);
  assert.equal(attempts, 2);
  assert.equal(sent.length, 2);
});

const TARGET_CHANGED_MESSAGE = 'Bağlantı hedefi değişti. Test için veritabanı parolasını yeniden girin.';
const DRAFT_OPERATIONS = [service.testConnectionDraft, service.getDatabases];

test('desktop draft target changes preserve only the known safe password reentry message', async (context) => {
  const { sent } = fixture(context);
  const requests = [];
  axios.defaults.adapter = async (config) => {
    requests.push(config);
    throw Object.assign(new Error('internal diagnostic'), {
      config,
      response: { status: 400, data: { error: TARGET_CHANGED_MESSAGE, detail: INFO.password } },
    });
  };
  for (const operation of DRAFT_OPERATIONS) {
    await assert.rejects(operation({ ...INFO, connectionId: 'saved', password: '' }), (error) => {
      assert.equal(error.message, TARGET_CHANGED_MESSAGE);
      assert.equal(error.response, undefined);
      assert.equal(error.config, undefined);
      assert.equal(error.cause, undefined);
      assert.doesNotMatch(error.stack, /internal diagnostic|transient-secret/);
      return true;
    });
  }
  assert.equal(requests.length, DRAFT_OPERATIONS.length);
  assert.equal(sent.length, DRAFT_OPERATIONS.length);
  for (const request of requests) {
    assert.match(request.url, /\/api\/connections\/(test|databases)$/);
    assert.equal(request.method, 'post');
    assert.equal(JSON.parse(request.data).bridgeId, BRIDGE_ID);
  }
});

test('desktop draft errors hide unknown messages, near matches and unexpected statuses', async (context) => {
  fixture(context);
  for (const response of [
    { status: 400, data: { error: `Password=${INFO.password}` } },
    { status: 400, data: { error: `${TARGET_CHANGED_MESSAGE} Password=${INFO.password}` } },
    { status: 400, data: { message: TARGET_CHANGED_MESSAGE } },
    { status: 500, data: { error: TARGET_CHANGED_MESSAGE } },
  ]) {
    axios.defaults.adapter = async (config) => {
      throw Object.assign(new Error(INFO.password), { config, response });
    };
    for (const operation of DRAFT_OPERATIONS) {
      await assert.rejects(operation(INFO), (error) => {
        assert.equal(error.name, 'DesktopConnectionError');
        assert.match(error.message, /Bu bilgisayardan bağlantı kurulamadı/);
        assert.equal(error.response, undefined);
        assert.equal(error.config, undefined);
        assert.doesNotMatch(error.stack, /transient-secret/);
        return true;
      });
    }
  }
});

test('desktop unsuccessful draft responses do not expose server diagnostics', async (context) => {
  fixture(context);
  axios.defaults.adapter = async (config) => ({
    data: { success: false, error: INFO.password, message: INFO.password },
    status: 200, statusText: 'OK', headers: {}, config,
  });
  for (const operation of DRAFT_OPERATIONS) {
    await assert.rejects(operation(INFO), (error) => {
      assert.equal(error.name, 'DesktopConnectionError');
      assert.doesNotMatch(error.message, /transient-secret/);
      return true;
    });
  }
});

test('browser draft failures preserve the original error without native requests', async (context) => {
  const { sent } = fixture(context, false);
  for (const message of [TARGET_CHANGED_MESSAGE, 'Browser validation error']) {
    const originalError = Object.assign(new Error('Bad Request'), {
      response: { status: 400, data: { error: message } },
    });
    axios.defaults.adapter = async () => { throw originalError; };
    for (const operation of DRAFT_OPERATIONS) {
      await assert.rejects(operation(INFO), (error) => error === originalError);
    }
  }
  assert.equal(sent.length, 0);
});

test('desktop form retains ordinary workflow without bridge controls or implicit save during test', async () => {
  const source = await readFile(new URL('../src/pages/SqlConnectionSettings.jsx', import.meta.url), 'utf8');
  assert.match(source, /if \(!isDesktop\) loadBridges\(\)/);
  assert.match(source, /\{!isDesktop && <div className="bridge-section">/);
  assert.match(source, /\{!isDesktop && <div className="form-group">\s*<small className="form-hint">/);
  assert.match(source, /const result = isDesktop\s*\? await testDesktopConnectionForm\(formData, savedConnectionId\)\s*: await testConnection\(await persistConnection\(\)\)/);
  assert.match(source, /onClick=\{handleTest\}/);
  assert.match(source, /onClick=\{handleSave\}/);
  assert.doesNotMatch(source, /<select[^>]*name="(?:connectionMode|bridgeId)"/);
});
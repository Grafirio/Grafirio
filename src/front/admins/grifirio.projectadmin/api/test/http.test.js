const test = require('node:test');
const assert = require('node:assert/strict');
const { createServiceMonitor } = require('../src/monitor/createServiceMonitor');
const { createServiceOperations } = require('../src/operations/createServiceOperations');
const { createRecoveryGuard } = require('../src/auth/createRecoveryGuard');
const { createApp } = require('../src/http/createApp');
const { loadConfig } = require('../src/config/loadConfig');
const { createFixtures } = require('./helpers');

const RECOVERY_KEY = 'test-recovery-key';

async function startServer({ composeUpResult = { exitCode: 0, output: '' } } = {}) {
  const fixtures = createFixtures();
  fixtures.docker.setContainer('db.container', { exists: true, running: true, status: 'running', health: null });
  const config = { ...loadConfig({}), allowedOrigins: ['http://localhost:59265'], smtp: { enabled: false }, composeFile: 'compose.yml', composeDir: '.' };
  const monitor = createServiceMonitor({
    registry: fixtures.registry, docker: fixtures.docker,
    probe: async () => ({ up: true, latencyMs: 1, detail: null, error: null }),
    settings: fixtures.settings, audit: fixtures.audit, notifier: fixtures.notifier, intervalMs: 1000,
  });
  const composeCalls = [];
  const operations = createServiceOperations({
    registry: fixtures.registry, docker: fixtures.docker, settings: fixtures.settings, audit: fixtures.audit, monitor,
    composeUp: async (options) => { composeCalls.push(options); return composeUpResult; },
    startDockerDesktop: async () => {}, config,
  });
  const verifyToken = async (token) => {
    if (token === 'admin-token') return { id: 'admin', isPlatformAdmin: true };
    if (token === 'user-token') return { id: 'user', isPlatformAdmin: false };
    throw new Error('invalid token');
  };
  const app = createApp({
    config, monitor, operations, settings: fixtures.settings, audit: fixtures.audit, verifyToken,
    recoveryGuard: createRecoveryGuard({ recoveryKey: RECOVERY_KEY }), logger: { error: () => {} },
  });
  const server = await new Promise((resolve) => { const instance = app.listen(0, '127.0.0.1', () => resolve(instance)); });
  const baseUrl = `http://127.0.0.1:${server.address().port}`;
  return { fixtures, composeCalls, baseUrl, close: () => new Promise((resolve) => server.close(resolve)) };
}

const call = (baseUrl, path, { method = 'GET', token, headers = {}, body } = {}) => fetch(`${baseUrl}${path}`, {
  method,
  headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}), ...headers },
  body: body ? JSON.stringify(body) : undefined,
});

test('operator endpoints require a valid token with the PLATFORM_ADMIN role', async () => {
  const server = await startServer();
  try {
    assert.equal((await call(server.baseUrl, '/api/ops/status')).status, 401);
    assert.equal((await call(server.baseUrl, '/api/ops/status', { token: 'garbage' })).status, 401);
    assert.equal((await call(server.baseUrl, '/api/ops/status', { token: 'user-token' })).status, 403);
    assert.equal((await call(server.baseUrl, '/api/ops/services/db/stop', { method: 'POST', token: 'user-token' })).status, 403);
    assert.equal((await call(server.baseUrl, '/api/ops/status', { token: 'admin-token' })).status, 200);
  } finally {
    await server.close();
  }
});

test('the public summary needs no token and leaks no service names', async () => {
  const server = await startServer();
  try {
    const response = await call(server.baseUrl, '/api/ops/status/summary');
    assert.equal(response.status, 200);
    assert.doesNotMatch(JSON.stringify(await response.json()), /container|Database/);
  } finally {
    await server.close();
  }
});

test('control actions only accept registry ids and never reach the docker client with arbitrary names', async () => {
  const server = await startServer();
  try {
    const injection = await call(server.baseUrl, `/api/ops/services/${encodeURIComponent('db.container; calc')}/stop`, { method: 'POST', token: 'admin-token' });
    assert.equal(injection.status, 404);
    assert.equal((await call(server.baseUrl, '/api/ops/services/db/format', { method: 'POST', token: 'admin-token' })).status, 404);
    assert.equal(server.fixtures.docker.calls.length, 0);

    const stop = await call(server.baseUrl, '/api/ops/services/db/stop', { method: 'POST', token: 'admin-token' });
    assert.equal(stop.status, 202);
    assert.deepEqual(server.fixtures.docker.calls, [{ name: 'db.container', action: 'stop' }]);
    assert.equal(server.fixtures.settings.isManuallyStopped('db'), true);

    await call(server.baseUrl, '/api/ops/services/db/start', { method: 'POST', token: 'admin-token' });
    assert.equal(server.fixtures.settings.isManuallyStopped('db'), false);

    const audit = await (await call(server.baseUrl, '/api/ops/audit', { token: 'admin-token' })).json();
    assert.deepEqual(audit.map((entry) => entry.action), ['start', 'stop', 'format', 'stop']);
    assert.deepEqual(audit.map((entry) => entry.outcome), ['succeeded', 'succeeded', 'failed', 'failed']);
    assert.equal(audit[0].actor.id, 'admin');
  } finally {
    await server.close();
  }
});

test('control is refused with 409 when Docker is not running', async () => {
  const server = await startServer();
  try {
    server.fixtures.docker.available = false;
    const response = await call(server.baseUrl, '/api/ops/services/db/restart', { method: 'POST', token: 'admin-token' });
    assert.equal(response.status, 409);
    assert.equal(server.fixtures.docker.calls.length, 0);
    const [entry] = server.fixtures.audit.readRecent();
    assert.equal(entry.outcome, 'failed');
    assert.equal(entry.actor.id, 'admin');
    assert.equal(entry.target, 'db');
    assert.equal(entry.action, 'restart');
  } finally {
    await server.close();
  }
});

test('a missing container is audited without attempting a Docker action', async () => {
  const server = await startServer();
  try {
    const response = await call(server.baseUrl, '/api/ops/services/api/start', { method: 'POST', token: 'admin-token' });
    assert.equal(response.status, 409);
    assert.equal(server.fixtures.docker.calls.length, 0);
    const [entry] = server.fixtures.audit.readRecent();
    assert.equal(entry.target, 'api');
    assert.equal(entry.outcome, 'failed');
    assert.match(entry.detail, /Container does not exist/);
  } finally {
    await server.close();
  }
});

test('auto-heal settings are validated and audited', async () => {
  const server = await startServer();
  try {
    const invalid = await call(server.baseUrl, '/api/ops/settings/autoheal', { method: 'PUT', token: 'admin-token', body: { maxAttempts: 999 } });
    assert.equal(invalid.status, 400);

    const valid = await call(server.baseUrl, '/api/ops/settings/autoheal', { method: 'PUT', token: 'admin-token', body: { enabled: true, maxAttempts: 5 } });
    assert.equal(valid.status, 200);
    assert.equal((await valid.json()).enabled, true);
  } finally {
    await server.close();
  }
});

test('recovery needs the key, is locked out after repeated failures, and only brings infrastructure up', async () => {
  const server = await startServer();
  try {
    assert.equal((await call(server.baseUrl, '/recovery/infrastructure/up', { method: 'POST' })).status, 401);
    assert.equal((await call(server.baseUrl, '/recovery/infrastructure/up', { method: 'POST', headers: { 'X-Recovery-Key': 'wrong' } })).status, 401);
    assert.equal((await call(server.baseUrl, '/recovery/services/db/stop', { method: 'POST', headers: { 'X-Recovery-Key': RECOVERY_KEY } })).status, 404);

    const accepted = await call(server.baseUrl, '/recovery/infrastructure/up', { method: 'POST', headers: { 'X-Recovery-Key': RECOVERY_KEY } });
    assert.equal(accepted.status, 202);
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(server.composeCalls[0].serviceNames, ['db']);

    for (let attempt = 0; attempt < 5; attempt += 1) {
      await call(server.baseUrl, '/recovery/status', { headers: { 'X-Recovery-Key': 'wrong' } });
    }
    const locked = await call(server.baseUrl, '/recovery/status', { headers: { 'X-Recovery-Key': RECOVERY_KEY } });
    assert.equal(locked.status, 429);
  } finally {
    await server.close();
  }
});

test('recovery is disabled when no key is configured', async () => {
  const guard = createRecoveryGuard({ recoveryKey: null });
  let status = null;
  guard({ socket: { remoteAddress: '127.0.0.1' }, get: () => '' }, { status: (code) => { status = code; return { json: () => {} }; } }, () => {});
  assert.equal(status, 404);
});

test('recovery refuses non-loopback callers even with the correct key', () => {
  const guard = createRecoveryGuard({ recoveryKey: RECOVERY_KEY });
  let status = null;
  guard({ socket: { remoteAddress: '172.18.0.5' }, get: () => RECOVERY_KEY }, { status: (code) => { status = code; return { json: () => {} }; } }, () => {});
  assert.equal(status, 403);
});

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { createTelemetryArchive } = require('../src/telemetry/createTelemetryArchive');
const { createApp } = require('../src/http/createApp');

const producer = { id: 'data-analysis', services: ['data-analysis', 'bridge'], key: 'a'.repeat(40) };
const event = (patch = {}) => ({ id: randomUUID(), timestamp: new Date().toISOString(), service: 'data-analysis',
  instanceId: 'replica-1', environment: 'Testing', version: '1.0.0', kind: 'log', name: 'application.log', ...patch });
const fixture = () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'grafirio-telemetry-'));
  const archive = createTelemetryArchive(directory);
  return { archive, directory };
};

test('archive survives collector restart and deduplicates retry while preserving replicas', () => {
  const { archive, directory } = fixture();
  const a = event(), b = { ...a, instanceId: 'replica-2' };
  assert.equal(archive.ingest({ events: [a, b] }, producer).inserted, 2);
  assert.equal(archive.ingest({ events: [a] }, producer).duplicates, 1);
  archive.close();
  const reopened = createTelemetryArchive(directory);
  try {
    assert.equal(reopened.query().total, 2);
    assert.equal(reopened.query({ instanceId: 'replica-2' }).total, 1);
  } finally { reopened.close(); }
});

test('archive strips raw secrets/messages and rejects cross-service spoofing atomically', () => {
  const { archive } = fixture();
  try {
    archive.ingest({ events: [event({ message: 'password=customer-secret', sql: 'SELECT private', attributes: { token: 'customer-secret' } })] }, producer);
    assert.doesNotMatch(JSON.stringify(archive.query()), /customer-secret|SELECT private/);
    assert.throws(() => archive.ingest({ events: [event(), event({ service: 'identity' })] }, producer), /Invalid telemetry envelope/);
    assert.equal(archive.query().total, 1);
    assert.throws(() => archive.query({ from: 'invalid' }), /Invalid time range/);
    assert.throws(() => archive.query({ service: { malicious: true } }), /Invalid filter/);
  } finally { archive.close(); }
});

test('Bridge correlation requires a trusted dispatch for the same firm AND Bridge', () => {
  const { archive } = fixture();
  const requestId = randomUUID(), bridgeId = randomUUID(), diagnosticId = randomUUID(), queryId = randomUUID();
  const traceId = '1'.repeat(32);
  try {
    archive.ingest({ events: [
      event({ name: 'query.correlation', diagnosticId, queryId, traceId }),
      event({ name: 'bridge.dispatch', queryId, traceId, requestId, companyId: 'firm-a', bridgeId }),
      event({ service: 'bridge', name: 'bridge.query.completed', requestId, companyId: 'firm-a', bridgeId }),
      event({ service: 'bridge', name: 'bridge.query.failed', requestId, companyId: 'firm-b', bridgeId }),
      event({ service: 'bridge', name: 'bridge.query.failed', requestId, companyId: 'firm-a', bridgeId: randomUUID() }),
    ] }, producer);
    const matches = archive.query({ diagnosticId }).items;
    assert.equal(matches.length, 3);
    assert.equal(matches.filter((item) => item.service === 'bridge').length, 1);
    assert.equal(matches.find((item) => item.service === 'bridge').companyId, 'firm-a');
  } finally { archive.close(); }
});

test('retention expires synthetic history and volume limits visibly evict records', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'grafirio-telemetry-'));
  let clock = Date.now();
  const archive = createTelemetryArchive(directory, { now: () => clock });
  try {
    archive.ingest({ events: [event()] }, producer);
    archive.updateSettings({ retentionDays: 1, maxRows: 100, maxBytes: 1048576 });
    clock += 2 * 86400000;
    archive.prune();
    assert.equal(archive.status().count, 0);
    assert.equal(archive.status().loss.expired, 1);
    for (let batch = 0; batch < 2; batch++) archive.ingest({ events: Array.from({ length: 75 }, () => event({ timestamp: new Date(clock).toISOString() })) }, producer);
    assert.equal(archive.status().count, 100);
    assert.equal(archive.status().loss.evicted, 50);
  } finally { archive.close(); }
});

test('pagination uses a stable sequence boundary while producers continue writing', () => {
  const { archive } = fixture();
  try {
    archive.ingest({ events: [event(), event()] }, producer);
    const first = archive.query({ page: 1, limit: 1 });
    archive.ingest({ events: [event()] }, producer);
    const second = archive.query({ page: 2, limit: 1, snapshot: first.snapshot });
    assert.equal(second.total, 2);
    assert.notEqual(second.items[0].id, first.items[0].id);
  } finally { archive.close(); }
});

test('metric dimensions are allowlisted and environment/version remain independently filterable', () => {
  const { archive } = fixture();
  try {
    archive.ingest({ events: [event({ kind: 'metric', name: 'metric', metric: { name: 'llm.duration', type: 'Histogram', count: 2, sum: 3,
      tags: { 'llm.outcome': 'error', authorization: 'customer-secret', sql: 'SELECT private' } } })] }, producer);
    const row = archive.query({ environment: 'Testing', version: '1.0.0', kind: 'metric' }).items[0];
    assert.deepEqual(row.metric.tags, { 'llm.outcome': 'error' });
    assert.equal(archive.query({ environment: 'Production' }).total, 0);
    assert.doesNotMatch(JSON.stringify(row), /customer-secret|SELECT private/);
  } finally { archive.close(); }
});

test('archive HTTP separates producer authentication from platform administration and audits retention', async () => {
  const { archive } = fixture(); const entries = [];
  const app = createApp({ config: { allowedOrigins: [], telemetryProducers: [producer] }, archive,
    monitor: {}, operations: {}, settings: {}, audit: { record: (entry) => entries.push(entry) }, recoveryGuard: (_req, res) => res.sendStatus(404),
    verifyToken: async (token) => ({ id: token, isPlatformAdmin: token === 'admin' }), logger: { error: () => {} } });
  const server = await new Promise((resolve) => { const handle = app.listen(0, '127.0.0.1', () => resolve(handle)); });
  const base = `http://127.0.0.1:${server.address().port}/api/telemetry`;
  try {
    assert.equal((await fetch(`${base}/events`)).status, 401);
    assert.equal((await fetch(`${base}/events`, { headers: { Authorization: 'Bearer company-user' } })).status, 403);
    assert.equal((await fetch(`${base}/ingest`, { method: 'POST', headers: { Authorization: 'Bearer admin' } })).status, 401);
    const ingest = await fetch(`${base}/ingest`, { method: 'POST', headers: { 'X-Grafirio-Telemetry-Key': producer.key, 'Content-Type': 'application/json' }, body: JSON.stringify({ events: [event()] }) });
    assert.equal(ingest.status, 200);
    const response = await fetch(`${base}/events`, { headers: { Authorization: 'Bearer admin' } });
    assert.equal(response.status, 200); assert.equal(response.headers.get('cache-control'), 'no-store');
    const settings = await fetch(`${base}/settings`, { method: 'PUT', headers: { Authorization: 'Bearer admin', 'Content-Type': 'application/json' }, body: JSON.stringify({ retentionDays: 2, maxRows: 1000, maxBytes: 1048576 }) });
    assert.equal(settings.status, 200);
    assert.equal(entries.at(-1).actor.id, 'admin');
    assert.equal(entries.at(-1).action, 'telemetry-retention');
  } finally { await new Promise((resolve) => server.close(resolve)); archive.close(); }
});

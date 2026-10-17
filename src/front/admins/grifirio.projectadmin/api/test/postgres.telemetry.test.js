const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { randomUUID, randomBytes } = require('node:crypto');
const { Pool } = require('pg');
const express = require('express');
const { createPostgresTelemetryArchive } = require('../src/telemetry/createPostgresTelemetryArchive');
const { createTelemetryRoutes } = require('../src/telemetry/createTelemetryRoutes');

test('PostgreSQL: durable commits, privacy, correlations, retention, concurrent retries and restricted audit',
  { skip: !process.env.TEST_TELEMETRY_POSTGRES_URL }, async () => {
    const url = new URL(process.env.TEST_TELEMETRY_POSTGRES_URL);
    assert.ok(['localhost', '127.0.0.1'].includes(url.hostname) && /^\/GrafirioEval_/i.test(url.pathname), 'Synthetic loopback database required');
    const admin = new Pool({ connectionString: url.href, ssl: false });
    const suffix = randomBytes(8).toString('hex'), schema = `telemetry_test_${suffix}`, role = `telemetry_test_${suffix}`;
    const password = randomBytes(32).toString('hex');
    let archive;
    const setup = await admin.connect();
    try {
      await setup.query(`CREATE SCHEMA ${schema}`);
      await setup.query(`SET search_path TO ${schema}`);
      await setup.query(fs.readFileSync(path.join(__dirname, '../src/telemetry/postgres-schema.sql'), 'utf8'));
      await setup.query(`CREATE ROLE ${role} LOGIN PASSWORD '${password}'`);
      await setup.query(`GRANT USAGE ON SCHEMA ${schema} TO ${role}`);
      await setup.query(`GRANT SELECT ON telemetry_schema TO ${role}`);
      await setup.query(`GRANT SELECT,INSERT,UPDATE,DELETE ON events,meta,producers TO ${role}`);
      await setup.query(`GRANT SELECT,INSERT ON audit TO ${role}`);
      await setup.query(`GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA ${schema} TO ${role}`);
      url.username = role; url.password = password; url.searchParams.set('options', `-c search_path=${schema}`);
      let clock = Date.now();
      const open = () => createPostgresTelemetryArchive(url.href, { ssl: false, now: () => clock });
      archive = await open();
      const producer = { id: 'data-analysis', services: ['data-analysis', 'bridge'] };
      const event = (patch = {}) => ({ id: randomUUID(), timestamp: new Date(clock).toISOString(), service: 'data-analysis',
        instanceId: 'replica-1', environment: 'Testing', version: '1.0.0', kind: 'log', name: 'application.log', ...patch });
      const a = event({ message: 'NEVER-PERSIST', sql: 'SELECT private' });
      const retries = await Promise.all(Array.from({ length: 4 }, () => archive.ingest({ events: [a] }, producer)));
      assert.equal(retries.reduce((sum, ack) => sum + ack.inserted, 0), 1);
      assert.equal(retries.reduce((sum, ack) => sum + ack.duplicates, 0), 3);
      await archive.close(); archive = await open();
      assert.equal((await archive.query()).total, 1);
      assert.doesNotMatch(JSON.stringify(await archive.query()), /NEVER-PERSIST|SELECT private/);
      await assert.rejects(archive.ingest({ events: [event(), event({ service: 'identity' })] }, producer), /Invalid telemetry envelope/);
      assert.equal((await archive.query()).total, 1);
      const diagnosticId = randomUUID(), queryId = randomUUID(), requestId = randomUUID(), bridgeId = randomUUID(), traceId = 'a'.repeat(32);
      await archive.ingest({ events: [event({ name: 'query.correlation', diagnosticId, queryId, traceId }),
        event({ name: 'bridge.dispatch', requestId, bridgeId, companyId: 'a', queryId, traceId }),
        event({ service: 'bridge', name: 'bridge.query.completed', requestId, bridgeId, companyId: 'a' }),
        event({ service: 'bridge', name: 'bridge.query.completed', requestId, bridgeId, companyId: 'b' }),
      ] }, producer);
      const linked = await archive.query({ diagnosticId });
      assert.equal(linked.total, 3);
      assert.deepEqual(linked.items.filter((row) => row.service === 'bridge').map((row) => row.companyId), ['a']);
      assert.equal((await archive.query({ environment: 'Testing', version: '1.0.0', companyId: 'b' })).total, 1);
      const first = await archive.query({ limit: 1 });
      await archive.ingest({ events: [event()] }, producer);
      assert.equal((await archive.query({ snapshot: first.snapshot, page: 2, limit: 1 })).total, first.total);
      await assert.rejects(archive.query({ from: 'invalid' }), /Invalid time range/);
      await assert.rejects(archive.updateSettings({ retentionDays: 0 }), /Invalid retention/);
      await archive.updateSettings({ retentionDays: 1, maxRows: 100, maxBytes: 1048576 });
      await archive.audit.record({ actor: { id: 'synthetic-admin' }, action: 'telemetry-retention', target: 'telemetry', outcome: 'succeeded' });
      const app = express();
      app.use('/api/telemetry', createTelemetryRoutes({ archive, audit: archive.audit,
        producers: [{ ...producer, key: 'a'.repeat(40) }],
        verifyToken: async (token) => ({ id: 'test-admin', isPlatformAdmin: token === 'admin' }) }));
      app.use((error, _req, res, _next) => res.status(error.status || 500).json({ error: 'safe test error' }));
      const server = app.listen(0, '127.0.0.1');
      await new Promise((resolve) => server.once('listening', resolve));
      const base = `http://127.0.0.1:${server.address().port}/api/telemetry`;
      try {
        assert.equal((await fetch(`${base}/status`)).status, 401);
        assert.equal((await fetch(`${base}/status`, { headers: { Authorization: 'Bearer company' } })).status, 403);
        assert.equal((await fetch(`${base}/events?from=invalid`, { headers: { Authorization: 'Bearer admin' } })).status, 400);
        const ack = await fetch(`${base}/ingest`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Grafirio-Telemetry-Key': 'a'.repeat(40) }, body: JSON.stringify({ events: [a] }) });
        assert.deepEqual(await ack.json(), { accepted: 1, inserted: 0, duplicates: 1 });
        const saved = await fetch(`${base}/settings`, { method: 'PUT', headers: { Authorization: 'Bearer admin', 'Content-Type': 'application/json' },
          body: JSON.stringify({ retentionDays: 1, maxRows: 100, maxBytes: 1048576 }) });
        assert.equal(saved.status, 200);
      } finally { await new Promise((resolve) => server.close(resolve)); }
      const restricted = new Pool({ connectionString: url.href, ssl: false });
      try {
        await assert.rejects(restricted.query('DELETE FROM audit'), /permission denied/);
        await assert.rejects(restricted.query('DROP TABLE events'), /must be owner/);
        await assert.rejects(restricted.query('CREATE TABLE forbidden(id int)'), /permission denied/);
      } finally { await restricted.end(); }
      clock += 2 * 86400000;
      await archive.prune();
      assert.equal((await archive.status()).count, 0);
      assert.equal((await archive.status()).loss.expired, 6);
      for (let index = 0; index < 2; index++) await archive.ingest({ events: Array.from({ length: 75 }, () => event()) }, producer);
      assert.equal((await archive.status()).count, 100);
      assert.equal((await archive.status()).loss.evicted, 50);
      assert.equal((await setup.query('SELECT count(*) FROM audit')).rows[0].count, '3');
    } finally {
      if (archive) await archive.close();
      // Only this test's randomly named synthetic schema/role are removed.
      await setup.query(`DROP SCHEMA ${schema} CASCADE`);
      await setup.query(`DROP ROLE IF EXISTS ${role}`);
      setup.release(); await admin.end();
    }
  });

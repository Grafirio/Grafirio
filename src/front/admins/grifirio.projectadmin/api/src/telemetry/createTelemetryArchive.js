const fs = require('node:fs');
const path = require('node:path');
const { DatabaseSync } = require('node:sqlite');
const { OpsError } = require('../OpsError');

const { buildTelemetryQuery } = require('./telemetryQuery');
const { DAY, SERVICES, guid, integer, normalizeEvent } = require('./telemetryContract');

function createTelemetryArchive(dataDir, { now = Date.now } = {}) {
  fs.mkdirSync(dataDir, { recursive: true });
  const db = new DatabaseSync(path.join(dataDir, 'telemetry.sqlite'));
  // A single collector owns this file. Producers/replicas never open it directly.
  db.exec(`PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA auto_vacuum=INCREMENTAL;
    PRAGMA max_page_count=32768;
    CREATE TABLE IF NOT EXISTS events (seq INTEGER PRIMARY KEY AUTOINCREMENT, eventKey TEXT UNIQUE NOT NULL,
      timestamp TEXT NOT NULL, receivedAt TEXT NOT NULL, service TEXT NOT NULL, instanceId TEXT NOT NULL,
      kind TEXT NOT NULL, level TEXT NOT NULL, traceId TEXT, diagnosticId TEXT, queryId TEXT, requestId TEXT,
      companyId TEXT, bridgeId TEXT, body TEXT NOT NULL, bytes INTEGER NOT NULL);
    CREATE INDEX IF NOT EXISTS event_time ON events(timestamp,seq);
    CREATE INDEX IF NOT EXISTS event_trace ON events(traceId);
    CREATE INDEX IF NOT EXISTS event_request ON events(requestId,companyId,bridgeId);
    CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS producers (producer TEXT NOT NULL, service TEXT NOT NULL, instanceId TEXT NOT NULL,
      receivedAt TEXT NOT NULL, dropped INTEGER NOT NULL, PRIMARY KEY(producer,service,instanceId));`);
  const getMeta = (key, fallback) => { const row = db.prepare('SELECT value FROM meta WHERE key=?').get(key); return row ? JSON.parse(row.value) : fallback; };
  const putMeta = (key, value) => db.prepare('INSERT INTO meta VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value').run(key, JSON.stringify(value));
  const settings = () => getMeta('settings', { retentionDays: 7, maxRows: 50000, maxBytes: 64 * 1024 * 1024 });
  function prune() {
    const policy = settings();
    const expired = db.prepare('DELETE FROM events WHERE timestamp < ?').run(new Date(now() - policy.retentionDays * DAY).toISOString()).changes;
    const evicted = db.prepare(`DELETE FROM events WHERE seq IN (SELECT seq FROM
      (SELECT seq,row_number() OVER (ORDER BY seq DESC) AS position,sum(bytes) OVER (ORDER BY seq DESC) AS payloadBytes FROM events)
      WHERE position>? OR payloadBytes>?)`).run(policy.maxRows, policy.maxBytes).changes;
    if (expired || evicted) putMeta('loss', { expired: (getMeta('loss', {}).expired || 0) + expired, evicted: (getMeta('loss', {}).evicted || 0) + evicted });
    db.prepare('DELETE FROM producers WHERE receivedAt < ?').run(new Date(now() - 30 * DAY).toISOString());
    db.exec('DELETE FROM producers WHERE rowid NOT IN (SELECT rowid FROM producers ORDER BY receivedAt DESC LIMIT 1000)');
  }
  function ingest(batch, producer) {
    if (!batch || !Array.isArray(batch.events) || batch.events.length > 100 || !integer(batch.dropped ?? 0, 0, Number.MAX_SAFE_INTEGER)) throw new OpsError(400, 'Invalid telemetry batch');
    const events = batch.events.map((event) => normalizeEvent(event, producer.services, now()));
    const receivedAt = new Date(now()).toISOString();
    let inserted = 0;
    db.exec('BEGIN IMMEDIATE');
    try {
      for (const event of events) {
        const body = JSON.stringify(event);
        inserted += db.prepare(`INSERT OR IGNORE INTO events(eventKey,timestamp,receivedAt,service,instanceId,kind,level,traceId,diagnosticId,queryId,requestId,companyId,bridgeId,body,bytes)
          VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)`).run(`${event.service}:${event.instanceId}:${event.id}`, event.timestamp, receivedAt,
          event.service, event.instanceId, event.kind, event.level, event.traceId, event.diagnosticId, event.queryId, event.requestId, event.companyId, event.bridgeId, body, Buffer.byteLength(body)).changes;
        db.prepare(`INSERT INTO producers VALUES (?,?,?,?,?) ON CONFLICT(producer,service,instanceId)
          DO UPDATE SET receivedAt=excluded.receivedAt,dropped=excluded.dropped`).run(producer.id, event.service, event.instanceId, receivedAt, batch.dropped ?? 0);
      }
      prune();
      db.exec('COMMIT');
    } catch (error) { db.exec('ROLLBACK'); throw error; }
    return { accepted: events.length, inserted, duplicates: events.length - inserted };
  }
  function query(filters = {}) {
    prune();
    const linksByField = {};
    for (const field of ['diagnosticId', 'queryId']) {
      if (guid(filters[field])) linksByField[field] = db.prepare(`SELECT DISTINCT traceId,queryId FROM events WHERE service='data-analysis'
        AND json_extract(body,'$.name')='query.correlation' AND ${field}=?`).all(guid(filters[field]));
    }
    const { where, args, snapshot, from, until, page, limit } = buildTelemetryQuery(filters, {
      now, snapshot: db.prepare('SELECT coalesce(max(seq),0) AS seq FROM events').get().seq, linksByField,
    });
    const total = db.prepare(`SELECT count(*) AS count FROM events WHERE ${where}`).get(...args).count;
    const items = db.prepare(`SELECT seq,body,receivedAt FROM events WHERE ${where} ORDER BY timestamp DESC,seq DESC LIMIT ? OFFSET ?`).all(...args, limit, (page - 1) * limit)
      .map((row) => ({ ...JSON.parse(row.body), sequence: row.seq, receivedAt: row.receivedAt }));
    return { items, total, page, limit, snapshot, from: new Date(from).toISOString(), until: new Date(until).toISOString() };
  }
  function updateSettings(patch) {
    if (!patch || !integer(patch.retentionDays, 1, 30) || !integer(patch.maxRows, 100, 100000)
      || !integer(patch.maxBytes, 1048576, 64 * 1024 * 1024)) throw new OpsError(400, 'Invalid retention policy');
    const value = { retentionDays: patch.retentionDays, maxRows: patch.maxRows, maxBytes: patch.maxBytes };
    putMeta('settings', value); // Cleanup is periodic, never a hidden side effect of the settings response.
    return value;
  }
  function status() {
    const totals = db.prepare('SELECT count(*) AS count,coalesce(sum(bytes),0) AS bytes,min(timestamp) AS oldest,max(timestamp) AS newest FROM events').get();
    return { ...totals, settings: settings(), loss: getMeta('loss', { expired: 0, evicted: 0 }),
      producers: db.prepare('SELECT * FROM producers ORDER BY receivedAt DESC').all(), services: SERVICES,
      storage: 'single-writer-sqlite', metricsMode: 'per-instance cumulative samples; do not sum samples over time' };
  }
  return { ingest, query, status, settings, updateSettings, prune, close: () => db.close() };
}
module.exports = { createTelemetryArchive, normalizeEvent };

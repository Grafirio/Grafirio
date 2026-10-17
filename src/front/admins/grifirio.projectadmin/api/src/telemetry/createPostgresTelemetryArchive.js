const { Pool } = require('pg');
const { OpsError } = require('../OpsError');
const { DAY, SERVICES, guid, integer, normalizeEvent } = require('./telemetryContract');
const { buildTelemetryQuery } = require('./telemetryQuery');

// Only SQL assembled by this module/shared predicate planner passes here. Values stay bound.
function postgresSql(sql) {
  let index = 0;
  return sql.replace(/json_extract\((\w+(?:\.\w+)?),'\$\.(\w+)'\)/g, "($1::jsonb->>'$2')")
    .replace(/\b(eventKey|receivedAt|instanceId|traceId|diagnosticId|queryId|requestId|companyId|bridgeId)\b/g, '"$1"')
    .replace(/\?/g, () => `$${++index}`);
}

async function createPostgresTelemetryArchive(connectionString, { now = Date.now, ssl = { rejectUnauthorized: true } } = {}) {
  const pool = new Pool({ connectionString, ssl, max: 3, connectionTimeoutMillis: 10000,
    idleTimeoutMillis: 30000, statement_timeout: 15000, application_name: 'grafirio-telemetry' });
  // A lost idle connection must not crash the collector or disclose connection details.
  pool.on('error', () => console.error('Telemetry database connection interrupted'));
  const run = (client, sql, args = []) => client.query(postgresSql(sql), args);
  const first = async (client, sql, args) => (await run(client, sql, args)).rows[0];
  async function transaction(operation) {
    const client = await pool.connect();
    try {
      await client.query('BEGIN');
      // Serialize writers, cleanup and settings across collector revisions/replicas.
      await client.query('SELECT pg_advisory_xact_lock(173542901)');
      const result = await operation(client);
      await client.query('COMMIT');
      return result;
    } catch (error) { await client.query('ROLLBACK').catch(() => {}); throw error; }
    finally { client.release(); }
  }
  // Schema creation is a deliberate provisioning step, never application startup DDL.
  try { await pool.query('SELECT version FROM telemetry_schema WHERE version=1'); }
  catch (error) { await pool.end(); throw error; }
  const getMeta = async (client, key, fallback) => (await first(client, 'SELECT value FROM meta WHERE key=?', [key]))?.value ?? fallback;
  const putMeta = (client, key, value) => run(client, 'INSERT INTO meta VALUES (?,?::jsonb) ON CONFLICT(key) DO UPDATE SET value=excluded.value', [key, JSON.stringify(value)]);
  const settingsFor = (client) => getMeta(client, 'settings', { retentionDays: 7, maxRows: 50000, maxBytes: 64 * 1024 * 1024 });
  async function pruneFor(client) {
    const policy = await settingsFor(client);
    const expired = (await run(client, 'DELETE FROM events WHERE timestamp < ?', [new Date(now() - policy.retentionDays * DAY).toISOString()])).rowCount;
    const evicted = (await run(client, `DELETE FROM events WHERE seq IN (SELECT seq FROM
      (SELECT seq,row_number() OVER (ORDER BY seq DESC) AS position,sum(bytes) OVER (ORDER BY seq DESC) AS payloadBytes FROM events) ranked
      WHERE position>? OR payloadBytes>?)`, [policy.maxRows, policy.maxBytes])).rowCount;
    if (expired || evicted) {
      const loss = await getMeta(client, 'loss', { expired: 0, evicted: 0 });
      await putMeta(client, 'loss', { expired: loss.expired + expired, evicted: loss.evicted + evicted });
    }
    await run(client, 'DELETE FROM producers WHERE receivedAt < ?', [new Date(now() - 30 * DAY).toISOString()]);
    await run(client, `DELETE FROM producers WHERE (producer,service,instanceId) NOT IN
      (SELECT producer,service,instanceId FROM producers ORDER BY receivedAt DESC LIMIT 1000)`);
  }
  async function ingest(batch, producer) {
    if (!batch || !Array.isArray(batch.events) || batch.events.length > 100 || !integer(batch.dropped ?? 0, 0, Number.MAX_SAFE_INTEGER)) throw new OpsError(400, 'Invalid telemetry batch');
    const events = batch.events.map((event) => normalizeEvent(event, producer.services, now()));
    return transaction(async (client) => {
      const receivedAt = new Date(now()).toISOString();
      const rows = events.map((event) => {
        const body = JSON.stringify(event);
        return { eventKey: `${event.service}:${event.instanceId}:${event.id}`, timestamp: event.timestamp, receivedAt,
          service: event.service, instanceId: event.instanceId, kind: event.kind, level: event.level, traceId: event.traceId,
          diagnosticId: event.diagnosticId, queryId: event.queryId, requestId: event.requestId, companyId: event.companyId,
          bridgeId: event.bridgeId, body, bytes: Buffer.byteLength(body) };
      });
      // Two bulk writes per batch, rather than 200 network round trips for 100 events.
      // A collector may be far from PostgreSQL; keep its durable acknowledgement within producer timeouts.
      const json = JSON.stringify(rows);
      const inserted = (await client.query(`INSERT INTO events("eventKey",timestamp,"receivedAt",service,"instanceId",kind,level,"traceId","diagnosticId","queryId","requestId","companyId","bridgeId",body,bytes)
        SELECT * FROM jsonb_to_recordset($1::jsonb) AS item("eventKey" text,timestamp text,"receivedAt" text,service text,
          "instanceId" text,kind text,level text,"traceId" text,"diagnosticId" text,"queryId" text,"requestId" text,
          "companyId" text,"bridgeId" text,body text,bytes integer) WHERE true ON CONFLICT("eventKey") DO NOTHING`, [json])).rowCount;
      await client.query(`INSERT INTO producers(producer,service,"instanceId","receivedAt",dropped)
        SELECT $1,service,"instanceId",$3::text,$4::bigint FROM
          (SELECT DISTINCT service,"instanceId" FROM jsonb_to_recordset($2::jsonb) AS item(service text,"instanceId" text)) instances
        ON CONFLICT(producer,service,"instanceId") DO UPDATE SET "receivedAt"=excluded."receivedAt",dropped=excluded.dropped`,
      [producer.id, json, receivedAt, batch.dropped ?? 0]);
      await pruneFor(client);
      return { accepted: events.length, inserted, duplicates: events.length - inserted };
    });
  }
  async function query(filters = {}) {
    return transaction(async (client) => {
      await pruneFor(client);
      const linksByField = {};
      for (const field of ['diagnosticId', 'queryId']) {
        if (guid(filters[field])) linksByField[field] = (await run(client, `SELECT DISTINCT traceId,queryId FROM events WHERE service='data-analysis'
          AND json_extract(body,'$.name')='query.correlation' AND ${field}=?`, [guid(filters[field])])).rows;
      }
      const { where, args, snapshot, from, until, page, limit } = buildTelemetryQuery(filters, {
        now, snapshot: Number((await first(client, 'SELECT coalesce(max(seq),0) AS seq FROM events')).seq), linksByField,
      });
      const total = Number((await first(client, `SELECT count(*) AS count FROM events WHERE ${where}`, args)).count);
      const items = (await run(client, `SELECT seq,body,receivedAt FROM events WHERE ${where} ORDER BY timestamp DESC,seq DESC LIMIT ? OFFSET ?`, [...args, limit, (page - 1) * limit])).rows
        .map((row) => ({ ...JSON.parse(row.body), sequence: Number(row.seq), receivedAt: row.receivedAt }));
      return { items, total, page, limit, snapshot, from: new Date(from).toISOString(), until: new Date(until).toISOString() };
    });
  }
  async function updateSettings(patch) {
    if (!patch || !integer(patch.retentionDays, 1, 30) || !integer(patch.maxRows, 100, 100000)
      || !integer(patch.maxBytes, 1048576, 64 * 1024 * 1024)) throw new OpsError(400, 'Invalid retention policy');
    const value = { retentionDays: patch.retentionDays, maxRows: patch.maxRows, maxBytes: patch.maxBytes };
    await transaction((client) => putMeta(client, 'settings', value));
    return value;
  }
  async function status() {
    return transaction(async (client) => {
      const totals = await first(client, 'SELECT count(*) AS count,coalesce(sum(bytes),0) AS bytes,min(timestamp) AS oldest,max(timestamp) AS newest FROM events');
      return { ...totals, count: Number(totals.count), bytes: Number(totals.bytes), settings: await settingsFor(client),
        loss: await getMeta(client, 'loss', { expired: 0, evicted: 0 }),
        producers: (await run(client, 'SELECT * FROM producers ORDER BY receivedAt DESC')).rows.map((row) => ({ ...row, dropped: Number(row.dropped) })),
        services: SERVICES, storage: 'managed-postgresql', metricsMode: 'per-instance cumulative samples; do not sum samples over time' };
    });
  }
  const audit = { async record({ actor, action, target, outcome, detail = null }) {
    // This table grants the runtime role INSERT/SELECT only; no retention DELETE or UPDATE.
    await pool.query('INSERT INTO audit(at,actor,action,target,outcome,detail) VALUES($1,$2,$3,$4,$5,$6)', [new Date(now()).toISOString(), actor, action, target, outcome, detail]);
  } };
  return { ingest, query, status, settings: () => transaction(settingsFor), updateSettings,
    prune: () => transaction(pruneFor), close: () => pool.end(), audit };
}
module.exports = { createPostgresTelemetryArchive };

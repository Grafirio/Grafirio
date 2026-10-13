const { Client } = require('pg');
const { openTcpConnection } = require('./openTcpConnection');

/** Runs `SELECT 1` when the password env var is set; otherwise falls back to a TCP check and says so. */
async function probePostgres(probe, timeoutMs, env) {
  const password = env[probe.passwordEnv];
  if (!password) {
    await openTcpConnection(probe.host, probe.port, timeoutMs);
    return { detail: `Port reachable; ${probe.passwordEnv} not set, query not verified` };
  }

  const client = new Client({
    host: probe.host,
    port: probe.port,
    database: probe.database,
    user: probe.user,
    password,
    connectionTimeoutMillis: timeoutMs,
    query_timeout: timeoutMs,
  });
  await client.connect();
  try {
    await client.query('SELECT 1');
    return {};
  } finally {
    await client.end();
  }
}

module.exports = { probePostgres };

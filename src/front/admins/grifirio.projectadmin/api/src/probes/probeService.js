const { openTcpConnection } = require('./openTcpConnection');
const { probeHttp } = require('./probeHttp');
const { probeMongo } = require('./probeMongo');
const { probePostgres } = require('./probePostgres');
const { probeRedis } = require('./probeRedis');

const PROBES = {
  tcp: (probe, timeoutMs) => openTcpConnection(probe.host, probe.port, timeoutMs).then(() => ({})),
  http: probeHttp,
  mongo: probeMongo,
  redis: probeRedis,
  postgres: probePostgres,
};

/**
 * Probes one service. Never throws: a failed probe is a normal result.
 * The error text is trimmed because driver messages can echo connection details.
 */
async function probeService(service, { timeoutMs, env }) {
  const startedAt = Date.now();
  try {
    const result = await PROBES[service.probe.type](service.probe, timeoutMs, env);
    return { up: true, latencyMs: Date.now() - startedAt, detail: result.detail ?? null, error: null };
  } catch (error) {
    return { up: false, latencyMs: Date.now() - startedAt, detail: null, error: String(error.message).slice(0, 200) };
  }
}

module.exports = { probeService };

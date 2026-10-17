// Dedicated collector: no Docker socket, recovery endpoint or service-control API.
const path = require('node:path');
const express = require('express');
const cors = require('cors');
const { loadConfig } = require('./src/config/loadConfig');
const { createTokenVerifier } = require('./src/auth/createTokenVerifier');
const { createAuditLog } = require('./src/audit/createAuditLog');
const { createTelemetryArchive } = require('./src/telemetry/createTelemetryArchive');
const { createTelemetryRoutes } = require('./src/telemetry/createTelemetryRoutes');
const { createPostgresTelemetryArchive } = require('./src/telemetry/createPostgresTelemetryArchive');
const { OpsError } = require('./src/OpsError');
async function main() {
const config = loadConfig();
const keys = config.telemetryProducers.map((producer) => producer.key);
if (keys.length !== 5 || keys.some((key) => key.length < 32) || new Set(keys).size !== keys.length) throw new Error('Telemetry producer keys must be distinct and at least 32 characters');
const archive = process.env.TELEMETRY_DATABASE_URL
  ? await createPostgresTelemetryArchive(process.env.TELEMETRY_DATABASE_URL)
  : createTelemetryArchive(path.join(config.dataDir, 'telemetry'));
const audit = archive.audit || createAuditLog(config.dataDir);
const app = express();
app.disable('x-powered-by');
app.use(cors({ origin: config.allowedOrigins, allowedHeaders: ['Authorization', 'Content-Type'] }));
app.get('/health', async (_req, res) => { try { await archive.status(); res.json({ status: 'ok' }); } catch { res.sendStatus(503); } });
app.use('/api/telemetry', createTelemetryRoutes({ archive, producers: config.telemetryProducers, verifyToken: createTokenVerifier(config), audit }));
// eslint-disable-next-line no-unused-vars
app.use((error, _req, res, _next) => {
  const status = error instanceof OpsError ? error.status : error.type === 'entity.too.large' ? 413 : error.type === 'entity.parse.failed' ? 400 : 500;
  res.status(status).json({ error: status === 500 ? 'Telemetry archive unavailable' : error.message });
});
const timer = setInterval(async () => { try { await archive.prune(); } catch { console.error('Telemetry cleanup failed'); } }, 60000);
const server = app.listen(config.port, config.bindHost);
for (const signal of ['SIGTERM', 'SIGINT']) process.once(signal, () => {
  clearInterval(timer); server.close(async () => { await archive.close(); process.exit(0); });
});
}
main().catch(() => { console.error('Telemetry collector startup failed; check database and producer configuration'); process.exitCode = 1; });

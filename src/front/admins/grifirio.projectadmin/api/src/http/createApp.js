const express = require('express');
const cors = require('cors');
const { OpsError } = require('../OpsError');
const { requirePlatformAdmin } = require('../auth/requirePlatformAdmin');
const { createAdminRoutes } = require('./routes/createAdminRoutes');
const { createRecoveryRoutes } = require('./routes/createRecoveryRoutes');
const { createTelemetryRoutes } = require('../telemetry/createTelemetryRoutes');

const HTTP_INTERNAL_ERROR = 500;
const JSON_BODY_LIMIT = '10kb';

function createApp({ config, monitor, operations, settings, audit, verifyToken, recoveryGuard, archive, logger = console }) {
  const app = express();
  app.disable('x-powered-by');
  app.use(cors({ origin: config.allowedOrigins, allowedHeaders: ['Authorization', 'Content-Type'] }));
  if (archive) app.use('/api/telemetry', createTelemetryRoutes({ archive, producers: config.telemetryProducers ?? [], verifyToken, audit }));
  app.use(express.json({ limit: JSON_BODY_LIMIT }));

  app.get('/health', (_req, res) => res.json({ status: 'ok' }));

  // Detail-free status for the end-user outage banner; deliberately unauthenticated.
  app.get('/api/ops/status/summary', (_req, res) => res.json(monitor.publicSummary()));

  app.use('/api/ops', requirePlatformAdmin(verifyToken), createAdminRoutes({ monitor, operations, settings, audit, config }));
  app.use('/recovery', createRecoveryRoutes({ monitor, operations, recoveryGuard }));

  // eslint-disable-next-line no-unused-vars
  app.use((error, _req, res, _next) => {
    if (error.type === 'entity.too.large') return res.status(413).json({ error: 'Request body too large' });
    if (error.type === 'entity.parse.failed') return res.status(400).json({ error: 'Invalid JSON' });
    if (error instanceof OpsError) return res.status(error.status).json({ error: error.message });
    logger.error('Unhandled request error', error);
    return res.status(HTTP_INTERNAL_ERROR).json({ error: 'Internal error' });
  });

  return app;
}

module.exports = { createApp };
